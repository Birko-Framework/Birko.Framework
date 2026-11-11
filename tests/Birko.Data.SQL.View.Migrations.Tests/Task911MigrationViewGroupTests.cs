using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

[Table("T911MigPersons")]
public class T911MigPerson : Birko.Data.Models.AbstractModel
{
    public string? Login { get; set; }
}

[Table("T911MigNotes")]
public class T911MigNote : Birko.Data.Models.AbstractModel
{
    public Guid PersonId { get; set; }
    public string? Note { get; set; }
}

[View(typeof(T911MigPerson), typeof(T911MigNote), nameof(T911MigPerson.Guid), nameof(T911MigNote.PersonId), name: "T911MigView")]
public class T911MigNotesView
{
    [ViewField(typeof(T911MigPerson), nameof(T911MigPerson.Login))]
    public string? Login { get; set; }

    [CountField(typeof(T911MigNote), nameof(T911MigNote.Note))]
    public int Notes { get; set; }

    [MinField(typeof(T911MigNote), nameof(T911MigNote.Note))]
    public string? FirstNote { get; set; }
}

[View(typeof(T911MigPerson), typeof(T911MigNote), nameof(T911MigPerson.Guid), nameof(T911MigNote.PersonId), name: "T911MigCountView")]
public class T911MigCountView
{
    [ViewField(typeof(T911MigPerson), nameof(T911MigPerson.Login))]
    public string? Login { get; set; }

    [CountField(typeof(T911MigNote), nameof(T911MigNote.Note))]
    public int Notes { get; set; }
}

/// <summary>
/// TASK-911 — a persistent view a migration creates groups by a string exactly as C# does, as the connector's own view DDL
/// does: <c>ViewSqlGenerator</c> renders its GROUP BY through the migration's connector's dialect. Persons whose logins are
/// twins under some collation (case, a trailing space, <c>ß</c> / <c>ss</c>, U+E000 and U+1F600 against <c>""</c>) each
/// keep their own row. Without a connector, a string group key is refused by name: the generator knows no provider, and the
/// plain GROUP BY would follow the column's collation.
/// </summary>
[Collection("T907Mig")]
public abstract class Task911MigrationViewGroupTestsBase : IDisposable
{
    protected readonly ITestOutputHelper Output;
    protected Task911MigrationViewGroupTestsBase(ITestOutputHelper output) => Output = output;

    protected abstract string Provider { get; }
    protected abstract string? HostVariable { get; }
    protected abstract PasswordSettings Settings();
    protected abstract AbstractConnector Connector(PasswordSettings settings);
    protected virtual char QuoteChar => ViewSqlGenerator.DefaultQuoteChar;

    private const string ViewName = "T911MigView";
    private AbstractConnector? _connector;

    private static readonly string[] Logins = { "Zebra", "zebra", "a", "a ", "ß", "ss", "", "", char.ConvertFromUtf32(0x1F600), "apple" };

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
        try { connector.DropTable(new[] { typeof(T911MigPerson), typeof(T911MigNote) }); } catch { }
    }

    private static string Show(string? value)
        => value == null ? "null" : "\"" + string.Concat(value.Select(c => c < 0x20 || c > 0x7E ? $"\\u{(int)c:X4}" : c.ToString())) + "\"";

    [Fact]
    public void A_migration_view_groups_by_a_string_exactly()
    {
        if (!Live()) return;
        var settings = Settings();
        var connector = Connector(settings);
        _connector = connector;
        Clean(connector);
        connector.CreateTable(new[] { typeof(T911MigPerson), typeof(T911MigNote) });
        var expected = new List<string>();
        for (var i = 0; i < Logins.Length; i++)
        {
            var person = new T911MigPerson { Guid = Guid.NewGuid(), Login = Logins[i] };
            connector.Insert(typeof(T911MigPerson), person);
            for (var j = 0; j <= i % 3; j++)
                connector.Insert(typeof(T911MigNote), new T911MigNote { Guid = Guid.NewGuid(), PersonId = person.Guid!.Value, Note = $"n{i}-{j}" });
            expected.Add($"{Show(Logins[i])}: {i % 3 + 1} {Show($"n{i}-0")}");
        }
        expected.Sort(StringComparer.Ordinal);

        using (var connection = connector.CreateConnection(settings))
        {
            connection.Open();
            using var tx = connection.BeginTransaction();
            new SqlMigrationContext(connection, tx, Provider, connector).CreateView(typeof(T911MigNotesView), quoteChar: QuoteChar);
            tx.Commit();
        }

        var actual = new List<string>();
        connector.DoCommand(c => c.CommandText = $"SELECT Login, Notes, FirstNote FROM {connector.QuoteIdentifier(ViewName)}", c =>
        {
            using var reader = c.ExecuteReader();
            while (reader.Read())
                actual.Add($"{Show(reader.IsDBNull(0) ? null : reader.GetString(0))}: {Convert.ToInt64(reader.GetValue(1))} {Show(reader.IsDBNull(2) ? null : reader.GetString(2))}");
        });
        actual.Sort(StringComparer.Ordinal);
        Output.WriteLine($"MEASURE {Provider} | {actual.Count} groups where C# makes {expected.Count} | missing [{string.Join(" | ", expected.Except(actual))}] | extra [{string.Join(" | ", actual.Except(expected))}]");

        actual.Should().Equal(expected);
    }

    public void Dispose()
    {
        if (_connector == null) return;
        Clean(_connector);
    }
}

public sealed class Task911MigrationViewWithoutConnectorTests
{
    [Fact]
    public void Without_a_connector_a_string_group_key_is_refused_by_name()
    {
        var act = () => ViewSqlGenerator.GenerateCreateViewSql(typeof(T911MigCountView));

        act.Should().Throw<NotSupportedException>().WithMessage("*T911MigCountView*T911MigCountView.Login*TASK-911*");
    }
}

public sealed class SqliteTask911MigrationViewGroupTests : Task911MigrationViewGroupTestsBase
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"birko-t911m-{Guid.NewGuid():N}.db");
    public SqliteTask911MigrationViewGroupTests(ITestOutputHelper output) : base(output) { }
    protected override string Provider => "SQLite";
    protected override string? HostVariable => null;
    protected override PasswordSettings Settings() => new SqLiteSettings(Path.GetDirectoryName(_path)!, Path.GetFileName(_path));
    protected override AbstractConnector Connector(PasswordSettings settings) => new SqLiteConnector((SqLiteSettings)settings);
}

public sealed class PostgreSqlTask911MigrationViewGroupTests : Task911MigrationViewGroupTestsBase
{
    public PostgreSqlTask911MigrationViewGroupTests(ITestOutputHelper output) : base(output) { }
    protected override string Provider => "PostgreSQL";
    protected override string? HostVariable => "BIRKO_PG_HOST";
    protected override PasswordSettings Settings() => new PostgreSqlSettings(Environment.GetEnvironmentVariable("BIRKO_PG_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_PG_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_PG_USER") ?? "postgres",
        Environment.GetEnvironmentVariable("BIRKO_PG_PASSWORD") ?? "postgres")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_PG_PORT"), out var p) ? p : 5432 };
    protected override AbstractConnector Connector(PasswordSettings settings) => new PostgreSQLConnector((PostgreSqlSettings)settings);
}

public sealed class MySqlTask911MigrationViewGroupTests : Task911MigrationViewGroupTestsBase
{
    public MySqlTask911MigrationViewGroupTests(ITestOutputHelper output) : base(output) { }
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

public sealed class MSSqlTask911MigrationViewGroupTests : Task911MigrationViewGroupTestsBase
{
    public MSSqlTask911MigrationViewGroupTests(ITestOutputHelper output) : base(output) { }
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
