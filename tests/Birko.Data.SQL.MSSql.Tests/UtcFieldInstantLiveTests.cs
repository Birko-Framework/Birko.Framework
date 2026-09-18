using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MSSql.Stores;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MSSql.Tests;

/// <summary>
/// TASK-263 — a <c>[UtcField]</c> property stores an <b>instant</b>, against a real SQL Server.
///
/// <para>
/// MSSql is the <i>other</i> provider with a genuinely timezone-aware type: <c>ConvertType</c> renders
/// <c>DbType.DateTimeOffset</c> as <c>DATETIMEOFFSET</c>, which stores the offset alongside the value. Before
/// TASK-263 nothing could reach that arm, because <c>CreateAbstractField</c> had no <c>DateTimeOffset</c> arm
/// and no attribute could override a field's <c>DbType</c>.
/// </para>
///
/// <para>
/// <b>Why this suite exists separately from the PostgreSQL one.</b> The read path is the part that differs per
/// provider, and MSSql is the reason <c>UtcDateTimeField.Read</c> is written the way it is: SqlClient
/// <b>throws</b> <c>InvalidCastException</c> for <c>GetDateTime</c> on a <c>datetimeoffset</c> column, so the
/// obvious implementation would work on PostgreSQL and fail outright here.
/// <c>GetFieldValue&lt;DateTimeOffset&gt;</c> is the only path that works on all four providers, and this suite
/// is what holds that claim honest for this one.
/// </para>
///
/// <para>
/// Gated on <c>BIRKO_MSSQL_HOST</c> (+ <c>_PORT</c> / <c>_USER</c> / <c>_PASSWORD</c> / <c>_DB</c>).
/// </para>
/// </summary>
public class UtcFieldInstantLiveTests : IDisposable
{
    private const string TableName = "UtcInstantRows";

    private static readonly DateTime Utc = new(2026, 3, 15, 10, 30, 0, DateTimeKind.Utc);

    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_MSSQL_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MSSQL_PORT"), out var p) ? p : 1433;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_MSSQL_USER") ?? "sa";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_MSSQL_PASSWORD") ?? "Birko!Passw0rd";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_MSSQL_DB") ?? "birkoview";
    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _output;

    public UtcFieldInstantLiveTests(ITestOutputHelper output) => _output = output;

    private bool RequireServer()
    {
        if (!string.IsNullOrWhiteSpace(Host)) return true;
        const string message = "SKIPPED: no live SQL Server. Set BIRKO_MSSQL_HOST to exercise this test; "
                             + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (RequireLive) throw new InvalidOperationException(message);
        return false;
    }

    /// <summary>TrustServerCertificate — a containerised SQL Server presents a self-signed certificate.</summary>
    private static MSSqlSettings Settings()
        => new(Host!, Database, User, Password, Port) { TrustServerCertificate = true };

    [Table(TableName)]
    public class StampRow : AbstractModel
    {
        [UtcField]
        public DateTime ObservedAt { get; set; }

        public DateTime NoticeDate { get; set; }
    }

    private static void Exec(string sql)
    {
        using var conn = new SqlConnection(Settings().GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static string ColumnType(string column)
    {
        using var conn = new SqlConnection(Settings().GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT data_type FROM information_schema.columns "
                        + $"WHERE table_name = '{TableName}' AND column_name = '{column}'";
        return cmd.ExecuteScalar()?.ToString() ?? "<none>";
    }

    private static void FreshTable()
    {
        Exec($"IF OBJECT_ID('{TableName}', 'U') IS NOT NULL DROP TABLE [{TableName}]");
        new MSSqlConnector(Settings()).CreateTable(new[] { typeof(StampRow) });
    }

    private static AsyncMSSqlStore<StampRow> AsyncStore()
    {
        var store = new AsyncMSSqlStore<StampRow>();
        store.SetSettings(Settings());
        return store;
    }

    private static StampRow Row() => new()
    {
        Guid = System.Guid.NewGuid(),
        ObservedAt = Utc,
        NoticeDate = new DateTime(2026, 3, 15, 10, 30, 0, DateTimeKind.Utc),
    };

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(Host)) return;
        try { Exec($"IF OBJECT_ID('{TableName}', 'U') IS NOT NULL DROP TABLE [{TableName}]"); } catch { }
    }

    [Fact]
    public void A_utc_field_declares_datetimeoffset_and_a_plain_one_declares_datetime2()
    {
        if (!RequireServer()) return;
        FreshTable();

        ColumnType(nameof(StampRow.ObservedAt)).Should().Be("datetimeoffset",
            "MSSql's own tz-aware type — ConvertType has always mapped DbType.DateTimeOffset here, and "
          + "TASK-263 is what finally made the arm reachable from a model");
        ColumnType(nameof(StampRow.NoticeDate)).Should().Be("datetime2",
            "an unmarked DateTime keeps TASK-256's wall-clock rule (CR-H086 chose DATETIME2 over DATE so the "
          + "time component survives)");
    }

    [Fact]
    public async Task A_utc_field_round_trips_the_instant_as_utc()
    {
        if (!RequireServer()) return;
        FreshTable();
        var store = AsyncStore();

        await store.CreateAsync(Row(), null, CancellationToken.None);
        var read = (await store.ReadAsync(CancellationToken.None)).Single();

        read.ObservedAt.Should().Be(Utc, "the instant must survive exactly");
        read.ObservedAt.Kind.Should().Be(DateTimeKind.Utc,
            "this is the assertion that pins why Read uses GetFieldValue<DateTimeOffset> rather than "
          + "GetDateTime: SqlClient throws InvalidCastException for GetDateTime on a datetimeoffset column, so "
          + "the obvious implementation would pass on PostgreSQL and fail here");
    }

    [Fact]
    public async Task A_plain_datetime_beside_it_still_reads_back_unspecified()
    {
        if (!RequireServer()) return;
        FreshTable();
        var store = AsyncStore();

        await store.CreateAsync(Row(), null, CancellationToken.None);
        var read = (await store.ReadAsync(CancellationToken.None)).Single();

        read.NoticeDate.Kind.Should().Be(DateTimeKind.Unspecified,
            "the two rules coexist per property — if this returns Utc, one has absorbed the other");
    }

    [Fact]
    public async Task A_utc_field_is_found_by_a_filter_bound_from_a_utc_kinded_value()
    {
        if (!RequireServer()) return;
        FreshTable();
        var store = AsyncStore();
        await store.CreateAsync(Row(), null, CancellationToken.None);

        var found = (await store.ReadAsync(x => x.ObservedAt == Utc, ct: CancellationToken.None)).ToList();

        found.Should().HaveCount(1, "the write and the filter bind through the same UtcDateTimeField.Write");
    }
}
