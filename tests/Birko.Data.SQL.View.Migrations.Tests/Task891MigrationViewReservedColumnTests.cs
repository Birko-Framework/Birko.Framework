using System;
using System.Data.Common;
using System.IO;
using System.Threading.Tasks;
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
using Microsoft.Data.Sqlite;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.View.Migrations.Tests;

[Table("T891MigLogins")]
public class T891MigLogin : Birko.Data.Models.AbstractModel
{
    public string? Login { get; set; }
}

[Table("T891MigOrders")]
public class T891MigOrder : Birko.Data.Models.AbstractModel
{
    public Guid LoginId { get; set; }
    public int Amount { get; set; }
}

/// <summary>A view property named <c>User</c>: the session user's function on PostgreSQL and SQL Server.</summary>
[View(typeof(T891MigLogin), typeof(T891MigOrder), nameof(T891MigLogin.Guid), nameof(T891MigOrder.LoginId), name: "T891MigView")]
public class T891MigUserView
{
    [ViewField(typeof(T891MigLogin), nameof(T891MigLogin.Login))]
    public string? User { get; set; }
}

/// <summary>A view property named <c>CURRENT_DATE</c>: the current date's function on SQLite.</summary>
[View(typeof(T891MigLogin), typeof(T891MigOrder), nameof(T891MigLogin.Guid), nameof(T891MigOrder.LoginId), name: "T891MigView")]
public class T891MigCurrentDateView
{
    [ViewField(typeof(T891MigLogin), nameof(T891MigLogin.Login))]
    public string? CURRENT_DATE { get; set; }
}

/// <summary>A view property named <c>CURRENT_USER</c>: the session user's function on MySQL.</summary>
[View(typeof(T891MigLogin), typeof(T891MigOrder), nameof(T891MigLogin.Guid), nameof(T891MigOrder.LoginId), name: "T891MigView")]
public class T891MigCurrentUserView
{
    [ViewField(typeof(T891MigLogin), nameof(T891MigLogin.Login))]
    public string? CURRENT_USER { get; set; }
}

/// <summary>The same view with a clean property name, which keeps working.</summary>
[View(typeof(T891MigLogin), typeof(T891MigOrder), nameof(T891MigLogin.Guid), nameof(T891MigOrder.LoginId), name: "T891MigView")]
public class T891MigCleanView
{
    [ViewField(typeof(T891MigLogin), nameof(T891MigLogin.Login))]
    public string? LoginName { get; set; }
}

/// <summary>
/// TASK-891 — a migration's <c>CreateView</c> refuses, before any DDL, a view whose own column (a view property) is a
/// name the provider rejects, through the connector's <c>RequireUsableViewColumnNames</c>, the check the connector's own
/// view DDL runs (TASK-872).
/// <para>
/// Before, measured live: <c>ViewSqlGenerator</c> emitted the view's columns bare (<c>AS User</c>) and checked nothing.
/// PostgreSQL and SQLite created the view, and its column read the session user or the current date in place of the
/// login (a filter on the function's value matched every row); MySQL and SQL Server refused the DDL with a syntax error.
/// Each case writes a <c>MEASURE</c> line.
/// </para>
/// </summary>
[Collection("T891Mig")]
public abstract class Task891MigrationViewReservedColumnTestsBase : IDisposable
{
    protected readonly ITestOutputHelper Output;
    protected Task891MigrationViewReservedColumnTestsBase(ITestOutputHelper output) => Output = output;

    protected abstract string Provider { get; }
    protected abstract string? HostVariable { get; }
    protected abstract PasswordSettings Settings();
    protected abstract AbstractConnector Connector(PasswordSettings settings);

    /// <summary>The view type whose property is this provider's value function.</summary>
    protected abstract Type ReservedView { get; }

    /// <summary>The value function's name, which is also the property's.</summary>
    protected abstract string FunctionName { get; }

    protected virtual char QuoteChar => ViewSqlGenerator.DefaultQuoteChar;

    private const string ViewName = "T891MigView";

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

    private AbstractConnector? Prepare()
    {
        if (!Live()) return null;
        _settings = Settings();
        var connector = Connector(_settings);
        _connector = connector;
        Clean(connector);
        connector.CreateTable(new[] { typeof(T891MigLogin), typeof(T891MigOrder) });
        var login = new T891MigLogin { Guid = Guid.NewGuid(), Login = "alice" };
        connector.Insert(typeof(T891MigLogin), login);
        connector.Insert(typeof(T891MigOrder), new T891MigOrder { Guid = Guid.NewGuid(), LoginId = login.Guid!.Value, Amount = 1 });
        return connector;
    }

    private static void Clean(AbstractConnector connector)
    {
        try { connector.DoCommand(c => c.CommandText = "DROP VIEW IF EXISTS " + connector.QuoteIdentifier(ViewName), c => c.ExecuteNonQuery()); } catch { }
        try { connector.DropTable(new[] { typeof(T891MigLogin), typeof(T891MigOrder) }); } catch { }
    }

    private void Migrate(AbstractConnector connector, Action<SqlMigrationContext> body)
    {
        using var connection = connector.CreateConnection(_settings!);
        connection.Open();
        using var tx = connection.BeginTransaction();
        body(new SqlMigrationContext(connection, tx, Provider, connector));
        tx.Commit();
    }

    private async Task MigrateAsync(AbstractConnector connector, Func<SqlMigrationContext, Task> body)
    {
        await using var connection = connector.CreateConnection(_settings!);
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();
        await body(new SqlMigrationContext(connection, tx, Provider, connector));
        await tx.CommitAsync();
    }

    private object? Scalar(AbstractConnector connector, string sql)
    {
        object? value = null;
        connector.DoCommand(c => c.CommandText = sql, c => value = c.ExecuteScalar());
        return value;
    }

    /// <summary>Whether reading the view fails: the provider-neutral probe, as this project has no provider view package.</summary>
    private bool ViewIsMissing(AbstractConnector connector)
    {
        try { Scalar(connector, $"SELECT COUNT(*) FROM {connector.QuoteIdentifier(ViewName)}"); return false; }
        catch (Exception) { return true; }
    }

    private string Measure(AbstractConnector connector, Exception? error)
    {
        if (error != null) return $"{error.GetType().Name}: {error.Message.Split('\n')[0]}";
        var view = connector.QuoteIdentifier(ViewName);
        var stored = Scalar(connector, $"SELECT {connector.QuoteIdentifier(FunctionName.ToLowerInvariant())} FROM {view}");
        var bare = Scalar(connector, $"SELECT {FunctionName} FROM {view}");
        var filtered = Scalar(connector, $"SELECT COUNT(*) FROM {view} WHERE {FunctionName} = (SELECT {FunctionName})");
        return $"created; the column holds '{stored}', read bare as Birko reads it gives '{bare}', and a filter on the "
            + $"function's value matched {filtered} of 1 row";
    }

    [Fact]
    public void A_view_with_a_rejected_column_is_refused_before_the_DDL()
    {
        var connector = Prepare();
        if (connector == null) return;

        Exception? error = null;
        try { Migrate(connector, context => context.CreateView(ReservedView, quoteChar: QuoteChar)); }
        catch (Exception ex) { error = ex; }
        Output.WriteLine($"MEASURE {Provider} | CreateView | {Measure(connector, error)}");

        error.Should().BeOfType<NotSupportedException>();
        error!.Message.Should().Contain($"\"{FunctionName}\"").And.Contain($"\"{ViewName}\"").And.Contain(Provider).And.Contain("reserved word");
        ViewIsMissing(connector).Should().BeTrue("no DDL ran");
    }

    [Fact]
    public async Task A_view_with_a_rejected_column_is_refused_before_the_DDL_async()
    {
        var connector = Prepare();
        if (connector == null) return;

        Exception? error = null;
        try { await MigrateAsync(connector, context => context.CreateViewAsync(ReservedView, quoteChar: QuoteChar)); }
        catch (Exception ex) { error = ex; }
        Output.WriteLine($"MEASURE {Provider} | CreateViewAsync | {Measure(connector, error)}");

        error.Should().BeOfType<NotSupportedException>();
        error!.Message.Should().Contain($"\"{FunctionName}\"").And.Contain(Provider);
        ViewIsMissing(connector).Should().BeTrue("no DDL ran");
    }

    [Fact]
    public void A_view_with_clean_columns_is_created()
    {
        var connector = Prepare();
        if (connector == null) return;

        Migrate(connector, context => context.CreateView(typeof(T891MigCleanView), quoteChar: QuoteChar));

        Scalar(connector, $"SELECT LoginName FROM {connector.QuoteIdentifier(ViewName)}").Should().Be("alice");
    }

    public void Dispose()
    {
        if (_connector == null) return;
        Clean(_connector);
    }
}

public sealed class SqliteTask891MigrationViewReservedColumnTests : Task891MigrationViewReservedColumnTestsBase
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"birko-t891m-{Guid.NewGuid():N}.db");
    public SqliteTask891MigrationViewReservedColumnTests(ITestOutputHelper output) : base(output) { }
    protected override string Provider => "SQLite";
    protected override string? HostVariable => null;
    protected override Type ReservedView => typeof(T891MigCurrentDateView);
    protected override string FunctionName => "CURRENT_DATE";
    protected override PasswordSettings Settings() => new SqLiteSettings(Path.GetDirectoryName(_path)!, Path.GetFileName(_path));
    protected override AbstractConnector Connector(PasswordSettings settings) => new SqLiteConnector((SqLiteSettings)settings);
}

public sealed class PostgreSqlTask891MigrationViewReservedColumnTests : Task891MigrationViewReservedColumnTestsBase
{
    public PostgreSqlTask891MigrationViewReservedColumnTests(ITestOutputHelper output) : base(output) { }
    protected override string Provider => "PostgreSQL";
    protected override string? HostVariable => "BIRKO_PG_HOST";
    protected override Type ReservedView => typeof(T891MigUserView);
    protected override string FunctionName => "User";
    protected override PasswordSettings Settings() => new PostgreSqlSettings(Environment.GetEnvironmentVariable("BIRKO_PG_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_PG_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_PG_USER") ?? "postgres",
        Environment.GetEnvironmentVariable("BIRKO_PG_PASSWORD") ?? "postgres")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_PG_PORT"), out var p) ? p : 5432 };
    protected override AbstractConnector Connector(PasswordSettings settings) => new PostgreSQLConnector((PostgreSqlSettings)settings);
}

public sealed class MySqlTask891MigrationViewReservedColumnTests : Task891MigrationViewReservedColumnTestsBase
{
    public MySqlTask891MigrationViewReservedColumnTests(ITestOutputHelper output) : base(output) { }
    protected override string Provider => "MySQL";
    protected override string? HostVariable => "BIRKO_MYSQL_HOST";
    protected override Type ReservedView => typeof(T891MigCurrentUserView);
    protected override string FunctionName => "CURRENT_USER";
    protected override char QuoteChar => '`';
    protected override PasswordSettings Settings() => new MySqlSettings(Environment.GetEnvironmentVariable("BIRKO_MYSQL_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_USER") ?? "root",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_PASSWORD") ?? "root")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MYSQL_PORT"), out var p) ? p : 3306 };
    protected override AbstractConnector Connector(PasswordSettings settings) => new MySQLConnector((MySqlSettings)settings);
}

public sealed class MSSqlTask891MigrationViewReservedColumnTests : Task891MigrationViewReservedColumnTestsBase
{
    public MSSqlTask891MigrationViewReservedColumnTests(ITestOutputHelper output) : base(output) { }
    protected override string Provider => "SQL Server";
    protected override string? HostVariable => "BIRKO_MSSQL_HOST";
    protected override Type ReservedView => typeof(T891MigUserView);
    protected override string FunctionName => "User";
    protected override PasswordSettings Settings() => new MSSqlSettings(Environment.GetEnvironmentVariable("BIRKO_MSSQL_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_USER") ?? "sa",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_PASSWORD") ?? "Birko!Passw0rd",
        int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MSSQL_PORT"), out var p) ? p : 1433)
    { TrustServerCertificate = true };
    protected override AbstractConnector Connector(PasswordSettings settings) => new MSSqlConnector((MSSqlSettings)settings);
}
