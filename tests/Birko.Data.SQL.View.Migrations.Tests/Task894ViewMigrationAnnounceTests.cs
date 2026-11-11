using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Migrations.SQL.Context;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MSSql.Stores;
using Birko.Data.SQL.MySQL.Stores;
using Birko.Data.SQL.PostgreSQL.Stores;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.SQL.View.Migrations;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.View.Migrations.Tests;

/// <summary>
/// TASK-894 — the view DDL a migration sends on its own connection reaches the connector's <c>OnExecute</c>.
/// Measured before, on SQLite, PostgreSQL 16, MySQL 8.4 and SQL Server 2022: <c>CreateView</c>, <c>DropView</c> and
/// their async twins announced nothing.
/// </summary>
public abstract class Task894ViewMigrationAnnounceTestsBase : IDisposable
{
    [Table("T894MigCustomers")]
    public class T894MigCustomer : Birko.Data.Models.AbstractModel
    {
        public string Name { get; set; } = null!;
    }

    [Table("T894MigOrders")]
    public class T894MigOrder : Birko.Data.Models.AbstractModel
    {
        public Guid CustomerId { get; set; }
    }

    [View(typeof(T894MigCustomer), typeof(T894MigOrder), nameof(T894MigCustomer.Guid), nameof(T894MigOrder.CustomerId), name: "T894MigView")]
    public class T894MigView
    {
        [ViewField(typeof(T894MigCustomer), nameof(T894MigCustomer.Name))]
        public string CustomerName { get; set; } = null!;
    }

    protected readonly ITestOutputHelper Output;
    protected Task894ViewMigrationAnnounceTestsBase(ITestOutputHelper output) => Output = output;

    protected abstract string Provider { get; }
    protected abstract string? HostVariable { get; }
    protected abstract AbstractConnector Connector();
    protected virtual char QuoteChar => '"';

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task View_DDL_in_a_migration_reaches_OnExecute(bool isAsync)
    {
        if (!Live()) return;
        _connector = Connector();
        _connector.CreateTable(new[] { typeof(T894MigCustomer), typeof(T894MigOrder) });
        var received = new List<string>();
        OnExecute record = text => { lock (received) received.Add(text); };
        using var connection = _connector.CreateConnection(_connector.Settings);
        connection.Open();
        _connector.OnExecute += record;
        try
        {
            using var tx = connection.BeginTransaction();
            var context = new SqlMigrationContext(connection, tx, Provider, _connector);
            if (isAsync)
            {
                await context.CreateViewAsync(typeof(T894MigView), quoteChar: QuoteChar);
                await context.DropViewAsync(typeof(T894MigView), quoteChar: QuoteChar);
                await context.CreateViewAsync(typeof(T894MigView), quoteChar: QuoteChar);
                await context.DropViewAsync("T894MigView", QuoteChar);
            }
            else
            {
                context.CreateView(typeof(T894MigView), quoteChar: QuoteChar);
                context.DropView(typeof(T894MigView), quoteChar: QuoteChar);
                context.CreateView(typeof(T894MigView), quoteChar: QuoteChar);
                context.DropView("T894MigView", QuoteChar);
            }
            tx.Commit();
        }
        finally { _connector.OnExecute -= record; }

        var creates = received.Count(t => t.Contains("CREATE VIEW", StringComparison.OrdinalIgnoreCase) && t.Contains("T894MigView", StringComparison.Ordinal));
        var drops = received.Count(t => t.Contains("DROP VIEW", StringComparison.OrdinalIgnoreCase) && t.Contains("T894MigView", StringComparison.Ordinal));
        Output.WriteLine($"MEASURE {Provider} | view migration ({(isAsync ? "async" : "sync")}) | CREATE VIEW announced {creates}/2 | DROP VIEW announced {drops}/4");
        foreach (var text in received) Output.WriteLine("  " + text);
        creates.Should().Be(2);
        drops.Should().Be(4, "CreateView drops before it creates, twice, and the migration drops twice");
    }

    public virtual void Dispose()
    {
        if (_connector == null) return;
        try { _connector.DropTable(new[] { typeof(T894MigCustomer), typeof(T894MigOrder) }); } catch { }
    }
}

public sealed class SqliteTask894ViewMigrationAnnounceTests : Task894ViewMigrationAnnounceTestsBase
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"birko-t894v-{Guid.NewGuid():N}.db");
    public SqliteTask894ViewMigrationAnnounceTests(ITestOutputHelper output) : base(output) { }
    protected override string Provider => "SQLite";
    protected override string? HostVariable => null;
    protected override AbstractConnector Connector() => new SqLiteConnector(new SqLiteSettings(Path.GetDirectoryName(_path)!, Path.GetFileName(_path)));

    public override void Dispose()
    {
        base.Dispose();
        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={_path}"));
        try { File.Delete(_path); } catch { }
    }
}

public sealed class PostgreSqlTask894ViewMigrationAnnounceTests : Task894ViewMigrationAnnounceTestsBase
{
    public PostgreSqlTask894ViewMigrationAnnounceTests(ITestOutputHelper output) : base(output) { }
    protected override string Provider => "PostgreSQL";
    protected override string? HostVariable => "BIRKO_PG_HOST";
    protected override AbstractConnector Connector() => new PostgreSQLConnector(new PostgreSqlSettings(Environment.GetEnvironmentVariable("BIRKO_PG_HOST")!,
        LiveDatabase.Name("PG"),
        Environment.GetEnvironmentVariable("BIRKO_PG_USER") ?? "postgres",
        Environment.GetEnvironmentVariable("BIRKO_PG_PASSWORD") ?? "postgres")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_PG_PORT"), out var p) ? p : 5432 });
}

public sealed class MySqlTask894ViewMigrationAnnounceTests : Task894ViewMigrationAnnounceTestsBase
{
    public MySqlTask894ViewMigrationAnnounceTests(ITestOutputHelper output) : base(output) { }
    protected override string Provider => "MySQL";
    protected override string? HostVariable => "BIRKO_MYSQL_HOST";
    protected override char QuoteChar => '`';
    protected override AbstractConnector Connector() => new MySQLConnector(new MySqlSettings(Environment.GetEnvironmentVariable("BIRKO_MYSQL_HOST")!,
        LiveDatabase.Name("MYSQL"),
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_USER") ?? "root",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_PASSWORD") ?? "root")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MYSQL_PORT"), out var p) ? p : 3306 });
}

public sealed class MSSqlTask894ViewMigrationAnnounceTests : Task894ViewMigrationAnnounceTestsBase
{
    public MSSqlTask894ViewMigrationAnnounceTests(ITestOutputHelper output) : base(output) { }
    protected override string Provider => "SQL Server";
    protected override string? HostVariable => "BIRKO_MSSQL_HOST";
    protected override AbstractConnector Connector() => new MSSqlConnector(new MSSqlSettings(Environment.GetEnvironmentVariable("BIRKO_MSSQL_HOST")!,
        LiveDatabase.Name("MSSQL"),
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_USER") ?? "sa",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_PASSWORD") ?? "Birko!Passw0rd",
        int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MSSQL_PORT"), out var p) ? p : 1433)
    { TrustServerCertificate = true });
}
