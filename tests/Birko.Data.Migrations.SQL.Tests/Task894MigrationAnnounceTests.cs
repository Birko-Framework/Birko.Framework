using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Migrations.Context;
using Birko.Data.Migrations.SQL.Settings;
using Birko.Data.SQL;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MSSql.Stores;
using Birko.Data.SQL.MySQL.Stores;
using Birko.Data.SQL.PostgreSQL.Stores;
using Birko.Data.SQL.SqLite.Stores;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.Migrations.SQL.Tests;

/// <summary>
/// TASK-894 — the statements a SQL migration sends on the runner's own connection reach the connector's
/// <c>OnExecute</c>, through <c>AbstractConnector.AnnounceCommand</c>, the one producer of what a subscriber receives.
/// <para>
/// Measured before, on SQLite, PostgreSQL 16, MySQL 8.4 and SQL Server 2022 (the <c>MEASURE</c> lines): none of them did
/// — a raw script, <c>CollectionExists</c>, <c>BulkInsert</c>, <c>CountDocuments</c>, <c>RenameField</c>, and the
/// migrations table's probe, create, read, insert and delete. Only the DDL routed through the connector
/// (<c>CreateCollection</c>, <c>DropCollection</c>) was announced.
/// </para>
/// </summary>
public abstract class Task894MigrationAnnounceTestsBase<DB> : IDisposable where DB : AbstractConnector
{
    protected readonly ITestOutputHelper Output;
    protected Task894MigrationAnnounceTestsBase(ITestOutputHelper output) => Output = output;

    protected abstract string Provider { get; }
    protected abstract string? HostVariable { get; }
    protected abstract Birko.Configuration.Settings Settings();

    // Lower case on purpose: the script creates them bare, and PostgreSQL folds a bare name.
    private readonly string _table = "t894m_" + Guid.NewGuid().ToString("N")[..8];
    private readonly string _ledger = "mig894_" + Guid.NewGuid().ToString("N")[..8];
    private readonly string _code = "c894-" + Guid.NewGuid().ToString("N")[..10];
    private AbstractConnector? _connector;

    private bool Live()
    {
        if (HostVariable == null || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(HostVariable))) return true;
        var message = $"SKIPPED: set {HostVariable} to run these live tests; set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        Output.WriteLine(message);
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
            throw new InvalidOperationException(message);
        return false;
    }

    private sealed class Script : SqlScriptMigration
    {
        private readonly string _table;
        public Script(string table) => _table = table;
        public override long Version => 1;
        public override string Name => "T894Script";
        protected override string UpSql => $"CREATE TABLE {_table} (id VARCHAR(36), code VARCHAR(50))";
    }

    private sealed class DataMigration : AbstractMigration
    {
        private readonly string _table;
        private readonly string _code;
        public DataMigration(string table, string code) { _table = table; _code = code; }
        public override long Version => 2;
        public override string Name => "T894Data";

        public override void Up(IMigrationContext context)
        {
            context.Schema.CollectionExists(_table).Should().BeTrue();
            context.Data.BulkInsert(_table, new[] { new Dictionary<string, object> { ["id"] = Guid.NewGuid().ToString(), ["code"] = _code } });
            context.Data.CountDocuments(_table).Should().Be(1);
            context.Schema.RenameField(_table, "code", "code2");
        }

        public override void Down(IMigrationContext context) { }
    }

    private void Drop()
    {
        if (_connector == null) return;
        using var connection = _connector.CreateConnection(_connector.Settings);
        connection.Open();
        foreach (var name in new[] { _table, _ledger })
        {
            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = "DROP TABLE " + _connector.QualifiedIdentifier(name);
                command.ExecuteNonQuery();
            }
            catch (DbException) { }
        }
    }

    public void Dispose() => Drop();

    private List<string> Missing(IReadOnlyCollection<string> received)
    {
        bool Has(params string[] parts) => received.Any(t => parts.All(p => t.Contains(p, StringComparison.OrdinalIgnoreCase)));
        var expected = new (string What, string[] Parts)[]
        {
            ("raw script", new[] { "CREATE TABLE", _table }),
            ("CollectionExists", new[] { _table.ToLowerInvariant() }),
            ("BulkInsert, with its values", new[] { "INSERT INTO", _table, _code }),
            ("CountDocuments", new[] { "COUNT", _table }),
            ("RenameField", new[] { "code2" }),
            ("migrations table probe or create", new[] { _ledger }),
            ("version row insert, with its values", new[] { "INSERT INTO", _ledger, "T894Data" }),
            ("version row delete", new[] { "DELETE FROM", _ledger }),
        };
        return expected.Where(e => !Has(e.Parts)).Select(e => e.What).ToList();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Every_statement_a_migration_sends_reaches_OnExecute(bool isAsync)
    {
        if (!Live()) return;
        _connector = DataBase.GetConnector<DB>(Settings());
        Drop();
        var runner = new SqlMigrationRunner(_connector, new SqlMigrationSettings { MigrationsTable = _ledger });
        runner.RegisterMigrations(new Script(_table), new DataMigration(_table, _code));
        var received = new List<string>();
        OnExecute record = text => { lock (received) received.Add(text); };
        _connector.OnExecute += record;
        try
        {
            if (isAsync)
            {
                await runner.InitializeAsync();
                (await runner.MigrateAsync()).Success.Should().BeTrue();
                (await runner.RollbackAsync(1)).Success.Should().BeTrue();
            }
            else
            {
                runner.Initialize();
                runner.Migrate().Success.Should().BeTrue();
                runner.Rollback(1).Success.Should().BeTrue();
            }
        }
        finally { _connector.OnExecute -= record; }

        var missing = Missing(received);
        Output.WriteLine($"MEASURE {Provider} | migration ({(isAsync ? "async" : "sync")}) | announced {received.Count} | missing {missing.Count}: {string.Join("; ", missing)}");
        foreach (var text in received) Output.WriteLine("  " + text);
        missing.Should().BeEmpty("every statement a migration sends is announced");
    }
}

public sealed class SqliteTask894MigrationAnnounceTests : Task894MigrationAnnounceTestsBase<SqLiteConnector>
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"birko-t894m-{Guid.NewGuid():N}.db");
    public SqliteTask894MigrationAnnounceTests(ITestOutputHelper output) : base(output) { }
    protected override string Provider => "SQLite";
    protected override string? HostVariable => null;
    protected override Birko.Configuration.Settings Settings() => new SqLiteSettings(Path.GetDirectoryName(_path)!, Path.GetFileName(_path));
}

public sealed class PostgreSqlTask894MigrationAnnounceTests : Task894MigrationAnnounceTestsBase<PostgreSQLConnector>
{
    public PostgreSqlTask894MigrationAnnounceTests(ITestOutputHelper output) : base(output) { }
    protected override string Provider => "PostgreSQL";
    protected override string? HostVariable => "BIRKO_PG_HOST";
    protected override Birko.Configuration.Settings Settings() => new PostgreSqlSettings(Environment.GetEnvironmentVariable("BIRKO_PG_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_PG_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_PG_USER") ?? "postgres",
        Environment.GetEnvironmentVariable("BIRKO_PG_PASSWORD") ?? "postgres")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_PG_PORT"), out var p) ? p : 5432 };
}

public sealed class MySqlTask894MigrationAnnounceTests : Task894MigrationAnnounceTestsBase<MySQLConnector>
{
    public MySqlTask894MigrationAnnounceTests(ITestOutputHelper output) : base(output) { }
    protected override string Provider => "MySQL";
    protected override string? HostVariable => "BIRKO_MYSQL_HOST";
    protected override Birko.Configuration.Settings Settings() => new MySqlSettings(Environment.GetEnvironmentVariable("BIRKO_MYSQL_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_USER") ?? "root",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_PASSWORD") ?? "root")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MYSQL_PORT"), out var p) ? p : 3306 };
}

public sealed class MSSqlTask894MigrationAnnounceTests : Task894MigrationAnnounceTestsBase<MSSqlConnector>
{
    public MSSqlTask894MigrationAnnounceTests(ITestOutputHelper output) : base(output) { }
    protected override string Provider => "SQL Server";
    protected override string? HostVariable => "BIRKO_MSSQL_HOST";
    protected override Birko.Configuration.Settings Settings() => new MSSqlSettings(Environment.GetEnvironmentVariable("BIRKO_MSSQL_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_USER") ?? "sa",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_PASSWORD") ?? "Birko!Passw0rd",
        int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MSSQL_PORT"), out var p) ? p : 1433)
    { TrustServerCertificate = true };
}
