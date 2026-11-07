using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MSSql.Stores;
using Birko.Data.SQL.Stores;
using Birko.Data.Stores;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MSSql.Tests;

/// <summary>
/// TASK-852: the multi-row insert re-binds the same parameter names for every row, and the SQL Server typing
/// (<c>DateTime2</c>, TASK-656) ran only when a parameter was first created. A nullable <c>DateTime</c> column whose
/// first row is null therefore sent every later row's value as an inferred <c>datetime</c>, rounded to ~3.33 ms.
/// The live tests are gated on <c>BIRKO_MSSQL_HOST</c>; the offline ones read the bound parameter.
/// </summary>
public class ReboundParameterTypeLiveTests
{
    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_MSSQL_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MSSQL_PORT"), out var p) ? p : 1433;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_MSSQL_USER") ?? "sa";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_MSSQL_PASSWORD") ?? "Birko!Passw0rd";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_MSSQL_DB") ?? "birkoview";

    private readonly ITestOutputHelper _output;
    public ReboundParameterTypeLiveTests(ITestOutputHelper output) => _output = output;

    private bool Live()
    {
        if (!string.IsNullOrWhiteSpace(Host)) return true;
        const string message = "SKIPPED: no live SQL Server. Set BIRKO_MSSQL_HOST; set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
            throw new InvalidOperationException(message);
        return false;
    }

    private static MSSqlSettings Settings() => new(Host!, Database, User, Password, Port) { TrustServerCertificate = true };

    private static void Exec(string sql)
    {
        using var connection = new SqlConnection(Settings().GetConnectionString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    [Table("T852Rebound")]
    public class T852Row : AbstractModel
    {
        public int Seq { get; set; }
        public DateTime? Stamp { get; set; }
        public decimal? Amount { get; set; }
    }

    private static readonly DateTime Precise = new DateTime(2026, 10, 17, 12, 34, 56, DateTimeKind.Utc).AddTicks(1234567);

    // The store's bulk create inserts row by row; the connector's Insert(table, models) is the multi-row insert that
    // re-binds one command's parameters per row.
    private (MSSqlConnector Connector, AsyncDataBaseBulkStore<MSSqlConnector, T852Row> Store) Setup()
    {
        Exec("DROP TABLE IF EXISTS [T852Rebound]");
        var connector = new MSSqlConnector(Settings());
        connector.CreateTable(new[] { typeof(T852Row) });
        var store = new AsyncDataBaseBulkStore<MSSqlConnector, T852Row>();
        store.SetSettings(Settings());
        return (connector, store);
    }

    private static T852Row Row(int seq, DateTime? stamp, decimal? amount = null)
        => new() { Guid = Guid.NewGuid(), Seq = seq, Stamp = stamp, Amount = amount };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_DateTime_after_a_null_in_one_multi_row_insert_keeps_every_tick(bool async)
    {
        if (!Live()) return;
        var (connector, store) = Setup();
        var stamped = Row(2, Precise);
        var rows = new object[] { Row(1, null), stamped };
        var table = DataBase.LoadTable(typeof(T852Row));
        if (async) await connector.InsertAsync(table, rows);
        else connector.Insert(table, rows);

        var read = await store.ReadFirstAsync(x => x.Guid == stamped.Guid);

        _output.WriteLine($"sent {Precise:O}, stored {read!.Stamp:O}");
        read.Stamp!.Value.Ticks.Should().Be(Precise.Ticks, "the re-bound parameter is typed DATETIME2 like a created one");
        (await store.ReadAsync(x => x.Stamp == Precise, null, null, null)).Should().ContainSingle();
    }

    [Fact]
    public async Task A_null_after_a_DateTime_and_a_decimal_after_a_null_are_stored_as_sent()
    {
        if (!Live()) return;
        var (connector, store) = Setup();
        var rows = new object[]
        {
            Row(1, Precise),
            Row(2, null, 12.3456m),
            Row(3, Precise.AddTicks(1), 7m),
        };
        await connector.InsertAsync(DataBase.LoadTable(typeof(T852Row)), rows);

        var read = (await store.ReadAsync(x => true, OrderBy<T852Row>.By(x => x.Seq), null, null)).ToList();

        read.Select(r => r.Stamp).Should().Equal(Precise, null, Precise.AddTicks(1));
        read.Select(r => r.Amount).Should().Equal(null, 12.3456m, 7m);
    }

    private static SqlParameter Rebind(object? first, object? second)
    {
        var connector = new MSSqlConnector(new MSSqlSettings("localhost", "db", "user", "pass"));
        using var command = new SqlCommand();
        connector.AddParameter(command, "@p", first);
        connector.AddParameter(command, "@p", second);
        return command.Parameters["@p"];
    }

    [Fact]
    public void A_parameter_rebound_from_null_to_a_DateTime_is_DateTime2()
    {
        Rebind(null, Precise).SqlDbType.Should().Be(SqlDbType.DateTime2);
    }

    [Fact]
    public void A_parameter_rebound_from_a_DateTime_to_another_type_drops_DateTime2()
    {
        var parameter = Rebind(Precise, 12.5m);

        parameter.SqlDbType.Should().Be(SqlDbType.Decimal, "a re-bound value is typed exactly as a newly created one");
        parameter.Value.Should().Be(12.5m);
    }

    [Fact]
    public void A_parameter_rebound_from_a_DateTime_to_null_binds_DBNull()
    {
        Rebind(Precise, null).Value.Should().Be(DBNull.Value);
    }
}
