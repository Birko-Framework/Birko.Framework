using System;
using System.IO;
using Birko.Configuration;
using Birko.Data.Migrations.SQL.Context;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MSSql.Stores;
using Birko.Data.SQL.MySQL.Stores;
using Birko.Data.SQL.PostgreSQL.Stores;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.SQL.View.Migrations;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.View.Migrations.Tests;

[Table("T907MigPersons")]
public class T907MigPerson : Birko.Data.Models.AbstractModel
{
    public string? Login { get; set; }
}

[Table("T907MigNotes")]
public class T907MigNote : Birko.Data.Models.AbstractModel
{
    public Guid PersonId { get; set; }
    public string? Note { get; set; }
}

[View(typeof(T907MigPerson), typeof(T907MigNote), nameof(T907MigPerson.Guid), nameof(T907MigNote.PersonId), name: "T907MigView")]
public class T907MigNotesView
{
    [ViewField(typeof(T907MigPerson), nameof(T907MigPerson.Login))]
    public string? Login { get; set; }

    [MinField(typeof(T907MigNote), nameof(T907MigNote.Note))]
    public string? FirstNote { get; set; }

    [MaxField(typeof(T907MigNote), nameof(T907MigNote.Note))]
    public string? LastNote { get; set; }
}

/// <summary>
/// TASK-907 — a persistent view a migration creates takes a string MIN / MAX in Ordinal order, as the connector's own view
/// DDL does: <c>ViewSqlGenerator</c> renders through the migration's connector's dialect. Measured before: it rendered the
/// portable plain <c>MIN</c> / <c>MAX</c>, which followed the column's collation (PostgreSQL and SQL Server answered
/// <c>apple</c> as the MIN of <c>apple</c> and <c>Banana</c>, where Ordinal answers <c>Banana</c>). Without a connector a
/// string MIN / MAX is refused by name.
/// </summary>
[Collection("T907Mig")]
public abstract class Task907MigrationViewStringOrderTestsBase : IDisposable
{
    protected readonly ITestOutputHelper Output;
    protected Task907MigrationViewStringOrderTestsBase(ITestOutputHelper output) => Output = output;

    protected abstract string Provider { get; }
    protected abstract string? HostVariable { get; }
    protected abstract PasswordSettings Settings();
    protected abstract AbstractConnector Connector(PasswordSettings settings);
    protected virtual char QuoteChar => ViewSqlGenerator.DefaultQuoteChar;

    private const string ViewName = "T907MigView";
    private PasswordSettings? _settings;
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

    private static void Clean(AbstractConnector connector)
    {
        try { connector.DoCommand(c => c.CommandText = "DROP VIEW IF EXISTS " + connector.QuoteIdentifier(ViewName), c => c.ExecuteNonQuery()); } catch { }
        try { connector.DropTable(new[] { typeof(T907MigPerson), typeof(T907MigNote) }); } catch { }
    }

    private object? Scalar(AbstractConnector connector, string sql)
    {
        object? value = null;
        connector.DoCommand(c => c.CommandText = sql, c => value = c.ExecuteScalar());
        return value;
    }

    [Fact]
    public void A_migration_view_takes_a_string_min_and_max_in_Ordinal_order()
    {
        if (!Live()) return;
        _settings = Settings();
        var connector = Connector(_settings);
        _connector = connector;
        Clean(connector);
        connector.CreateTable(new[] { typeof(T907MigPerson), typeof(T907MigNote) });
        var person = new T907MigPerson { Guid = Guid.NewGuid(), Login = "p" };
        connector.Insert(typeof(T907MigPerson), person);
        foreach (var note in new[] { "apple", "Banana", "a", "a " })
            connector.Insert(typeof(T907MigNote), new T907MigNote { Guid = Guid.NewGuid(), PersonId = person.Guid!.Value, Note = note });

        using (var connection = connector.CreateConnection(_settings))
        {
            connection.Open();
            using var tx = connection.BeginTransaction();
            new SqlMigrationContext(connection, tx, Provider, connector).CreateView(typeof(T907MigNotesView), quoteChar: QuoteChar);
            tx.Commit();
        }

        var view = connector.QuoteIdentifier(ViewName);
        var first = Scalar(connector, $"SELECT FirstNote FROM {view}") as string;
        var last = Scalar(connector, $"SELECT LastNote FROM {view}") as string;
        Output.WriteLine($"MEASURE {Provider} | MIN '{first}' MAX '{last}' | expected 'Banana' / 'apple'");

        (first, last).Should().Be(("Banana", "apple"));
    }

    public void Dispose()
    {
        if (_connector == null) return;
        Clean(_connector);
    }
}

public sealed class Task907MigrationViewWithoutConnectorTests
{
    [Fact]
    public void Without_a_connector_a_string_min_or_max_is_refused_by_name()
    {
        var act = () => ViewSqlGenerator.GenerateCreateViewSql(typeof(T907MigNotesView));

        act.Should().Throw<NotSupportedException>().WithMessage("*T907MigView*Min*T907MigNotesView.FirstNote*TASK-907*");
    }
}

public sealed class SqliteTask907MigrationViewStringOrderTests : Task907MigrationViewStringOrderTestsBase
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"birko-t907m-{Guid.NewGuid():N}.db");
    public SqliteTask907MigrationViewStringOrderTests(ITestOutputHelper output) : base(output) { }
    protected override string Provider => "SQLite";
    protected override string? HostVariable => null;
    protected override PasswordSettings Settings() => new SqLiteSettings(Path.GetDirectoryName(_path)!, Path.GetFileName(_path));
    protected override AbstractConnector Connector(PasswordSettings settings) => new SqLiteConnector((SqLiteSettings)settings);
}

public sealed class PostgreSqlTask907MigrationViewStringOrderTests : Task907MigrationViewStringOrderTestsBase
{
    public PostgreSqlTask907MigrationViewStringOrderTests(ITestOutputHelper output) : base(output) { }
    protected override string Provider => "PostgreSQL";
    protected override string? HostVariable => "BIRKO_PG_HOST";
    protected override PasswordSettings Settings() => new PostgreSqlSettings(Environment.GetEnvironmentVariable("BIRKO_PG_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_PG_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_PG_USER") ?? "postgres",
        Environment.GetEnvironmentVariable("BIRKO_PG_PASSWORD") ?? "postgres")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_PG_PORT"), out var p) ? p : 5432 };
    protected override AbstractConnector Connector(PasswordSettings settings) => new PostgreSQLConnector((PostgreSqlSettings)settings);
}

public sealed class MySqlTask907MigrationViewStringOrderTests : Task907MigrationViewStringOrderTestsBase
{
    public MySqlTask907MigrationViewStringOrderTests(ITestOutputHelper output) : base(output) { }
    protected override string Provider => "MySQL";
    protected override string? HostVariable => "BIRKO_MYSQL_HOST";
    protected override char QuoteChar => '`';
    protected override PasswordSettings Settings() => new MySqlSettings(Environment.GetEnvironmentVariable("BIRKO_MYSQL_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_USER") ?? "root",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_PASSWORD") ?? "root")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MYSQL_PORT"), out var p) ? p : 3306 };
    protected override AbstractConnector Connector(PasswordSettings settings) => new MySQLConnector((MySqlSettings)settings);
}

public sealed class MSSqlTask907MigrationViewStringOrderTests : Task907MigrationViewStringOrderTestsBase
{
    public MSSqlTask907MigrationViewStringOrderTests(ITestOutputHelper output) : base(output) { }
    protected override string Provider => "SQL Server";
    protected override string? HostVariable => "BIRKO_MSSQL_HOST";
    protected override PasswordSettings Settings() => new MSSqlSettings(Environment.GetEnvironmentVariable("BIRKO_MSSQL_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_USER") ?? "sa",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_PASSWORD") ?? "Birko!Passw0rd",
        int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MSSQL_PORT"), out var p) ? p : 1433)
    { TrustServerCertificate = true };
    protected override AbstractConnector Connector(PasswordSettings settings) => new MSSqlConnector((MSSqlSettings)settings);
}
