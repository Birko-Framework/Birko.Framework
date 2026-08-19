using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.PostgreSQL.Stores;
using FluentAssertions;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.PostgreSQL.Tests;

/// <summary>
/// TASK-263 — a <c>[UtcField]</c> property stores an <b>instant</b>, against a real PostgreSQL.
///
/// <para>
/// TASK-256 settled that a plain Birko <c>DateTime</c> column is a wall clock and named an opt-in for the case
/// it deliberately does not serve — a value whose instant must be unambiguous. This is the live half of that
/// opt-in: the column really is <c>timestamp with time zone</c>, the instant survives exactly, and the
/// read-back carries <c>DateTimeKind.Utc</c>.
/// </para>
///
/// <para>
/// <b>The interaction worth understanding before editing anything here.</b> TASK-256's
/// <c>NormalizeTimestampValue</c> strips <c>Kind</c> from every bound <c>DateTime</c> on this provider. A
/// <c>Kind=Utc</c> value stripped to <c>Unspecified</c> is inferred by Npgsql as <c>timestamp</c>, and
/// PostgreSQL re-reads that wall clock <b>in the session's time zone</b> when assigning it to a
/// <c>timestamptz</c> column — storing a different instant, with no error. <c>UtcDateTimeField.Write</c>
/// therefore binds a <c>DateTimeOffset</c>, which that helper's <c>is DateTime</c> test does not match. So the
/// two features compose only because of the bound value's CLR type, and
/// <see cref="A_utc_field_stores_the_same_instant_on_a_non_utc_server"/> is the test that would catch it
/// breaking.
/// </para>
///
/// <para>
/// Gated on <c>BIRKO_PG_HOST</c> (+ <c>_PORT</c> / <c>_USER</c> / <c>_PASSWORD</c> / <c>_DB</c>).
/// </para>
/// </summary>
public class UtcFieldInstantLiveTests : IDisposable
{
    private const string TableName = "UtcInstantRows";

    /// <summary>The instant every test writes.</summary>
    private static readonly DateTime Utc = new(2026, 3, 15, 10, 30, 0, DateTimeKind.Utc);

    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_PG_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_PG_PORT"), out var p) ? p : 5432;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_PG_USER") ?? "postgres";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_PG_PASSWORD") ?? "postgres";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_PG_DB") ?? "birkoview";
    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _output;

    public UtcFieldInstantLiveTests(ITestOutputHelper output) => _output = output;

    private bool RequireServer()
    {
        if (!string.IsNullOrWhiteSpace(Host)) return true;
        const string message = "SKIPPED: no live PostgreSQL. Set BIRKO_PG_HOST to exercise this test; "
                             + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (RequireLive) throw new InvalidOperationException(message);
        return false;
    }

    private static PostgreSqlSettings Settings(string? database = null)
        => new(Host!, database ?? Database, User, Password) { Port = Port };

    /// <summary>
    /// Both meanings on one entity (criterion 6): <c>ObservedAt</c> is an instant, <c>NoticeDate</c> a wall
    /// clock. Attribute-driven rather than fluent-mapped, because <c>[UtcField]</c> is an attribute and the
    /// mapping is what is under test.
    /// </summary>
    [Table(TableName)]
    public class StampRow : AbstractModel
    {
        [UtcField]
        public DateTime ObservedAt { get; set; }

        public DateTime NoticeDate { get; set; }
    }

    private static void Exec(string sql, string? database = null)
    {
        using var conn = new NpgsqlConnection(Settings(database).GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// The declared column type, from the catalogue rather than from a call that "did not throw" — this layer
    /// swallows a missing relation, so only the catalogue is evidence. The table name keeps its case (the DDL
    /// quotes it) while the column is folded (column definitions are emitted bare).
    /// </summary>
    private static string ColumnType(string column, string? database = null)
    {
        using var conn = new NpgsqlConnection(Settings(database).GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT data_type FROM information_schema.columns "
                        + $"WHERE table_name = '{TableName}' AND column_name = '{column.ToLowerInvariant()}'";
        return cmd.ExecuteScalar()?.ToString() ?? "<none>";
    }

    /// <summary>The stored value rendered as text, on a connection of its own.</summary>
    private static string StoredText(string column, string? database = null)
    {
        using var conn = new NpgsqlConnection(Settings(database).GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {column.ToLowerInvariant()}::text FROM \"{TableName}\" LIMIT 1";
        return cmd.ExecuteScalar()?.ToString() ?? "<null>";
    }

    private static void FreshTable(string? database = null)
    {
        Exec($"DROP TABLE IF EXISTS \"{TableName}\" CASCADE", database);
        new PostgreSQLConnector(Settings(database)).CreateTable(new[] { typeof(StampRow) });
    }

    private static AsyncPostgreSQLStore<StampRow> AsyncStore(string? database = null)
    {
        var store = new AsyncPostgreSQLStore<StampRow>();
        store.SetSettings(Settings(database));
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
        try { Exec($"DROP TABLE IF EXISTS \"{TableName}\" CASCADE"); } catch { }
    }

    // ============================================================ the column

    [Fact]
    public void A_utc_field_declares_a_timezone_aware_column_and_a_plain_one_does_not()
    {
        if (!RequireServer()) return;
        FreshTable();

        ColumnType(nameof(StampRow.ObservedAt)).Should().Be("timestamp with time zone",
            "[UtcField] maps to DbType.DateTimeOffset, which ConvertType renders as TIMESTAMPTZ — before "
          + "TASK-263 nothing could reach that arm");
        ColumnType(nameof(StampRow.NoticeDate)).Should().Be("timestamp without time zone",
            "an unmarked DateTime keeps TASK-256's wall-clock rule, on the same table");
    }

    // ============================================================ the instant

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
            "the whole point of the opt-in: unlike a plain DateTime column, this one says WHICH instant it "
          + "names, so it comes back UTC-kinded rather than Unspecified");
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
            "TASK-256's contract is unchanged for an unmarked property — if this starts returning Utc, the "
          + "two rules have merged and one of them was lost");
    }

    [Fact]
    public async Task A_utc_field_is_found_by_a_filter_bound_from_a_utc_kinded_value()
    {
        if (!RequireServer()) return;
        FreshTable();
        var store = AsyncStore();
        await store.CreateAsync(Row(), null, CancellationToken.None);

        var found = (await store.ReadAsync(x => x.ObservedAt == Utc, ct: CancellationToken.None)).ToList();

        found.Should().HaveCount(1,
            "the write path and the filter path must agree about the instant — the filter binds through the "
          + "same UtcDateTimeField.Write, so both send a DateTimeOffset");
    }

    // ============================================================ the discriminating test

    /// <summary>
    /// The instant must not depend on the server's session time zone. On a UTC server this passes whatever the
    /// binding does, so — exactly as in TASK-256 — it needs a deliberately non-UTC server, which
    /// <c>PostgreSqlSettings.GetConnectionString()</c> cannot express (it emits no <c>Timezone</c> key). Hence a
    /// dedicated throwaway database, and <c>ClearAllPools()</c>, without which a pooled connection keeps
    /// <c>Etc/UTC</c> and the test measures nothing.
    ///
    /// <para>
    /// This is the test that fails if <c>UtcDateTimeField.Write</c> stops returning a <c>DateTimeOffset</c>:
    /// a bare <c>DateTime</c> would be stripped of its <c>Kind</c> by TASK-256's helper and then re-read in
    /// this session's zone — one hour out, silently.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_utc_field_stores_the_same_instant_on_a_non_utc_server()
    {
        if (!RequireServer()) return;

        var tzDatabase = "birko_tz_task263";
        Exec($"DROP DATABASE IF EXISTS {tzDatabase} WITH (FORCE)", Database);
        Exec($"CREATE DATABASE {tzDatabase}", Database);
        try
        {
            Exec($"ALTER DATABASE {tzDatabase} SET TimeZone TO 'Europe/Bratislava'", Database);
            NpgsqlConnection.ClearAllPools();

            FreshTable(tzDatabase);
            var store = AsyncStore(tzDatabase);
            await store.CreateAsync(Row(), null, CancellationToken.None);

            var stored = StoredText(nameof(StampRow.ObservedAt), tzDatabase);
            _output.WriteLine($"TZ=Europe/Bratislava  stored={stored}");

            // Rendered in the session zone, so 11:30+01 -- which IS 10:30Z. The instant is what matters.
            var read = (await store.ReadAsync(CancellationToken.None)).Single();
            read.ObservedAt.Should().Be(Utc,
                "the stored instant must be independent of the session time zone; if Write stopped binding a "
              + "DateTimeOffset, TASK-256's Kind-stripper would make this an hour out with no error");
            read.ObservedAt.Kind.Should().Be(DateTimeKind.Utc);

            stored.Should().StartWith("2026-03-15 11:30:00+01",
                "pinned deliberately: PostgreSQL renders a timestamptz in the session zone, and 11:30+01 is "
              + "the same instant as 10:30Z. Asserting the rendering as well as the instant is what "
              + "distinguishes 'stored the right instant' from 'stored the right wall clock by luck'");
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            try { Exec($"DROP DATABASE IF EXISTS {tzDatabase} WITH (FORCE)", Database); } catch { }
        }
    }
}
