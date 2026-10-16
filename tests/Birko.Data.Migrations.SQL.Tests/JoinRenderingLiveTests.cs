using System;
using System.Data.Common;
using System.IO;
using System.Linq;
using Birko.Data.Models;
using Birko.Data.SQL;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Conditions;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MSSql.Stores;
using Birko.Data.SQL.MySQL.Stores;
using Birko.Data.SQL.PostgreSQL.Stores;
using Birko.Data.SQL.SqLite.Stores;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.Migrations.SQL.Tests;

[Table("T626Customers")]
public class T626Customer : AbstractModel
{
    public string? Name { get; set; }
}

[Table("T626Orders")]
public class T626Order : AbstractModel
{
    public Guid CustomerId { get; set; }
}

/// <summary>A view declaring its join keys and no connect: (ViewConnect.None, the default).</summary>
[View(typeof(T626Customer), typeof(T626Order), nameof(T626Customer.Guid), nameof(T626Order.CustomerId))]
public class T626CustomerOrderView
{
    [ViewField(typeof(T626Customer), nameof(T626Customer.Name))]
    public string? CustomerName { get; set; }

    [ViewField(typeof(T626Order), nameof(T626Order.Guid))]
    public Guid? OrderId { get; set; }
}

/// <summary>
/// TASK-626 (SH-M147, SH-M148), live, counted in rows. Three customers, two with one order each, one with none, so an
/// inner join is 2 rows, a left join 3, the cross product 6, and the bare FROM 3.
/// </summary>
public abstract class JoinRenderingLiveTestsBase : IDisposable
{
    protected readonly ITestOutputHelper Output;
    protected JoinRenderingLiveTestsBase(ITestOutputHelper output) => Output = output;

    protected abstract string? HostVariable { get; }
    protected abstract AbstractConnector Connector();
    protected abstract DbConnection OpenRaw();

    private static readonly Guid Ann = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Cid = Guid.Parse("33333333-3333-3333-3333-333333333333");

    protected bool RequireServer()
    {
        if (HostVariable == null || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(HostVariable))) return true;
        var message = $"SKIPPED: set {HostVariable} to run these live tests; set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        Output.WriteLine(message);
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
            throw new InvalidOperationException(message);
        return false;
    }

    private AbstractConnector Seed()
    {
        var connector = Connector();
        try { connector.DropTable(new[] { typeof(T626Customer), typeof(T626Order) }); } catch { }
        connector.CreateTable(new[] { typeof(T626Customer), typeof(T626Order) });
        using var connection = OpenRaw();
        var c = connector.QuoteIdentifier("T626Customers");
        var o = connector.QuoteIdentifier("T626Orders");
        Exec(connection, $"INSERT INTO {c} (Guid, Name) VALUES ('{Ann}', 'Ann'), ('{Bob}', 'Bob'), ('{Cid}', 'Cid')");
        Exec(connection, $"INSERT INTO {o} (Guid, CustomerId) VALUES ('{Guid.NewGuid()}', '{Ann}'), ('{Guid.NewGuid()}', '{Bob}')");
        return connector;
    }

    private static void Exec(DbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        if (HostVariable != null && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(HostVariable))) return;
        try { Connector().DropTable(new[] { typeof(T626Customer), typeof(T626Order) }); } catch { }
    }

    private static Condition On => Condition.AndField("T626Customers.Guid", "T626Orders.CustomerId");
    private static readonly string[] From = { "T626Customers" };

    [Fact]
    public void A_join_given_a_condition_and_no_type_is_an_inner_join()
    {
        if (!RequireServer()) return;
        var connector = Seed();

        connector.SelectCount(From, new[] { Join.Create("T626Customers", "T626Orders", On) }).Should().Be(2, "not the product (6)");
        connector.SelectCount(From, new[] { new Join("T626Customers", "T626Orders", conditions: new[] { On }) }).Should().Be(2);
        connector.SelectCount(From, new[] { Join.Create("T626Customers", "T626Orders", On, JoinType.LeftOuter) }).Should().Be(3);
    }

    [Fact]
    public void A_conditionless_cross_join_is_the_product()
    {
        if (!RequireServer()) return;
        var connector = Seed();

        connector.SelectCount(From, new[] { Join.Create("T626Customers", "T626Orders", JoinType.Cross) }).Should().Be(6,
            "the join was skipped, so the count was the bare FROM (3)");
    }

    [Fact]
    public void A_cross_join_with_conditions_and_an_inner_join_without_are_refused()
    {
        if (!RequireServer()) return;
        var connector = Seed();

        connector.Invoking(c => c.SelectCount(From, new[] { Join.Create("T626Customers", "T626Orders", On, JoinType.Cross) }))
            .Should().Throw<Exception>().Where(e => e is NotSupportedException || e.InnerException is NotSupportedException);
        connector.Invoking(c => c.SelectCount(From, new[] { Join.Create("T626Customers", "T626Orders", JoinType.Inner) }))
            .Should().Throw<Exception>().Where(e => e is NotSupportedException || e.InnerException is NotSupportedException);
    }

    /// <summary>TASK-151 (SH-M403): an offset with no limit skips; it was dropped, so the read returned page 1.</summary>
    [Fact]
    public void An_offset_without_a_limit_skips_rows()
    {
        if (!RequireServer()) return;
        var connector = Seed();

        var rows = connector.Select(typeof(T626Customer), (System.Collections.Generic.IEnumerable<Condition>?)null,
            new System.Collections.Generic.Dictionary<string, bool> { ["Name"] = false }, limit: null, offset: 1).Cast<T626Customer>().ToList();

        rows.Select(r => r.Name).Should().Equal("Bob", "Cid");
    }

    [Fact]
    public void An_attribute_view_declared_without_connect_is_an_inner_join()
    {
        if (!RequireServer()) return;
        var connector = Seed();
        var sql = Birko.Data.SQL.Connectors.ViewSelectSqlBuilder.BuildViewSelectSql(DataBase.LoadView(typeof(T626CustomerOrderView)), connector.QuoteIdentifier);
        Output.WriteLine(sql);

        using var connection = OpenRaw();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM ({sql}) v";
        Convert.ToInt64(command.ExecuteScalar()).Should().Be(2, "a [View] with join keys joins on them, not the product (6)");

        // TASK-151 (SH-M404): the view's count is the count of its rows, through COUNT(*) over its own SELECT; both
        // joined tables carry a Guid column, which a derived table may not repeat on SQL Server.
        connector.SelectCount(DataBase.LoadView(typeof(T626CustomerOrderView))).Should().Be(2);
    }
}

public sealed class SqliteJoinRenderingLiveTests : JoinRenderingLiveTestsBase
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"birko-t626-{Guid.NewGuid():N}.db");
    public SqliteJoinRenderingLiveTests(ITestOutputHelper output) : base(output) { }
    private SqLiteSettings Settings() => new(Path.GetDirectoryName(_path)!, Path.GetFileName(_path));
    protected override string? HostVariable => null;
    protected override AbstractConnector Connector() => DataBase.GetConnector<SqLiteConnector>(Settings());
    protected override DbConnection OpenRaw() { var c = new SqliteConnection(Settings().GetConnectionString()); c.Open(); return c; }
}

public sealed class PostgreSqlJoinRenderingLiveTests : JoinRenderingLiveTestsBase
{
    public PostgreSqlJoinRenderingLiveTests(ITestOutputHelper output) : base(output) { }
    private static PostgreSqlSettings Settings() => new(Environment.GetEnvironmentVariable("BIRKO_PG_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_PG_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_PG_USER") ?? "postgres",
        Environment.GetEnvironmentVariable("BIRKO_PG_PASSWORD") ?? "postgres")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_PG_PORT"), out var p) ? p : 5432 };
    protected override string? HostVariable => "BIRKO_PG_HOST";
    protected override AbstractConnector Connector() => DataBase.GetConnector<PostgreSQLConnector>(Settings());
    protected override DbConnection OpenRaw() { var c = new NpgsqlConnection(Settings().GetConnectionString()); c.Open(); return c; }
}

public sealed class MySqlJoinRenderingLiveTests : JoinRenderingLiveTestsBase
{
    public MySqlJoinRenderingLiveTests(ITestOutputHelper output) : base(output) { }
    private static MySqlSettings Settings() => new(Environment.GetEnvironmentVariable("BIRKO_MYSQL_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_USER") ?? "root",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_PASSWORD") ?? "root")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MYSQL_PORT"), out var p) ? p : 3306 };
    protected override string? HostVariable => "BIRKO_MYSQL_HOST";
    protected override AbstractConnector Connector() => DataBase.GetConnector<MySQLConnector>(Settings());
    protected override DbConnection OpenRaw() { var c = new MySqlConnection(Settings().GetConnectionString()); c.Open(); return c; }
}

public sealed class MSSqlJoinRenderingLiveTests : JoinRenderingLiveTestsBase
{
    public MSSqlJoinRenderingLiveTests(ITestOutputHelper output) : base(output) { }
    private static MSSqlSettings Settings() => new(Environment.GetEnvironmentVariable("BIRKO_MSSQL_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_USER") ?? "sa",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_PASSWORD") ?? "Birko!Passw0rd",
        int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MSSQL_PORT"), out var p) ? p : 1433)
    { TrustServerCertificate = true };
    protected override string? HostVariable => "BIRKO_MSSQL_HOST";
    protected override AbstractConnector Connector() => DataBase.GetConnector<MSSqlConnector>(Settings());
    protected override DbConnection OpenRaw() { var c = new SqlConnection(Settings().GetConnectionString()); c.Open(); return c; }
}
