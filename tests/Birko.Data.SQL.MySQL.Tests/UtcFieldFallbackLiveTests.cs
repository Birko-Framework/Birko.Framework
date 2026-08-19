using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MySQL.Stores;
using FluentAssertions;
using MySqlConnector;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MySQL.Tests;

/// <summary>
/// TASK-263 — the <b>fallback contract</b> for <c>[UtcField]</c> on MySQL, which has no timezone-aware column
/// type this framework maps to. Criterion 3: what is stored and what a read returns must be stated and tested,
/// because a silent degrade to a timezone-less column is the failure mode this family of findings is about.
///
/// <para>
/// <b>The contract.</b> The <b>instant</b> survives exactly and reads back as <c>DateTimeKind.Utc</c>, the same
/// as on PostgreSQL and MSSql. A caller's original offset does not survive — normalised away uniformly on every
/// provider, because a field cannot behave differently per provider.
/// </para>
///
/// <para>
/// <b>What MySQL does, measured.</b> <c>ConvertType</c> renders <c>DbType.DateTimeOffset</c> as plain
/// <c>DATETIME</c>, and MySqlConnector <i>accepts</i> a <c>DateTimeOffset</c> parameter against it, dropping the
/// offset and storing the UTC wall clock (<c>2026-03-15 10:30:00</c>). The instant is therefore recoverable only
/// because both sides agree the column holds UTC — which is exactly what the attribute declares. Note the read
/// cannot use <c>GetDateTime</c> here: it returns <c>Unspecified</c>, so interpreting it would depend on the
/// reader's own time zone. <c>GetFieldValue&lt;DateTimeOffset&gt;</c> is what makes it exact.
/// </para>
///
/// <para>
/// <b>A session time zone is set deliberately.</b> MySQL's <c>DATETIME</c> is not converted on storage, so a
/// non-UTC session should change nothing — and asserting that is the difference between a contract and a
/// coincidence.
/// </para>
///
/// <para>Gated on <c>BIRKO_MYSQL_HOST</c> (+ <c>_PORT</c> / <c>_USER</c> / <c>_PASSWORD</c> / <c>_DB</c>).</para>
/// </summary>
public class UtcFieldFallbackLiveTests : IDisposable
{
    private const string TableName = "UtcFallbackRows";

    private static readonly DateTime Utc = new(2026, 3, 15, 10, 30, 0, DateTimeKind.Utc);

    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_MYSQL_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MYSQL_PORT"), out var p) ? p : 3306;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_MYSQL_USER") ?? "root";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_MYSQL_PASSWORD") ?? "root";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_MYSQL_DB") ?? "birkoview";
    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _output;

    public UtcFieldFallbackLiveTests(ITestOutputHelper output) => _output = output;

    private bool RequireServer()
    {
        if (!string.IsNullOrWhiteSpace(Host)) return true;
        const string message = "SKIPPED: no live MySQL. Set BIRKO_MYSQL_HOST to exercise this test; "
                             + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (RequireLive) throw new InvalidOperationException(message);
        return false;
    }

    private static MySqlSettings Settings() => new(Host!, Database, User, Password, Port);

    [Table(TableName)]
    public class StampRow : AbstractModel
    {
        [UtcField]
        public DateTime ObservedAt { get; set; }

        public DateTime NoticeDate { get; set; }
    }

    private static void Exec(string sql)
    {
        using var conn = new MySqlConnection(Settings().GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static string Scalar(string sql)
    {
        using var conn = new MySqlConnection(Settings().GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar()?.ToString() ?? "<null>";
    }

    private static void FreshTable()
    {
        Exec($"DROP TABLE IF EXISTS `{TableName}`");
        new MySQLConnector(Settings()).CreateTable(new[] { typeof(StampRow) });
    }

    private static AsyncMySQLStore<StampRow> AsyncStore()
    {
        var store = new AsyncMySQLStore<StampRow>();
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
        try { Exec($"DROP TABLE IF EXISTS `{TableName}`"); } catch { }
    }

    // ============================================================ the promise that IS kept

    [Fact]
    public async Task The_instant_survives_exactly_and_reads_back_as_utc()
    {
        if (!RequireServer()) return;
        FreshTable();
        var store = AsyncStore();

        await store.CreateAsync(Row(), null, CancellationToken.None);
        var read = (await store.ReadAsync(CancellationToken.None)).Single();

        read.ObservedAt.Should().Be(Utc, "the instant is the promise, on every provider");
        read.ObservedAt.Kind.Should().Be(DateTimeKind.Utc,
            "GetDateTime returns Unspecified on this column, so a read built on it would depend on the "
          + "reader's own time zone — GetFieldValue<DateTimeOffset> is what makes this exact");
    }

    [Fact]
    public async Task A_plain_datetime_beside_it_is_still_a_wall_clock()
    {
        if (!RequireServer()) return;
        FreshTable();
        var store = AsyncStore();

        await store.CreateAsync(Row(), null, CancellationToken.None);
        var read = (await store.ReadAsync(CancellationToken.None)).Single();

        read.NoticeDate.Kind.Should().Be(DateTimeKind.Unspecified,
            "TASK-256's rule is untouched for an unmarked property");
    }

    // ============================================================ the promise that is NOT kept

    [Fact]
    public async Task The_original_offset_is_normalised_away_not_preserved()
    {
        if (!RequireServer()) return;
        FreshTable();
        var store = AsyncStore();
        var local = new DateTime(2026, 3, 15, 11, 30, 0, DateTimeKind.Local);
        var row = Row();
        row.ObservedAt = local;

        await store.CreateAsync(row, null, CancellationToken.None);
        var read = (await store.ReadAsync(CancellationToken.None)).Single();

        read.ObservedAt.Should().Be(local.ToUniversalTime(), "the instant is preserved");
        read.ObservedAt.Kind.Should().Be(DateTimeKind.Utc,
            "and returns as UTC rather than the Local it went in as — the offset is gone, by design");
    }

    // ============================================================ what the storage actually looks like

    [Fact]
    public async Task The_column_is_a_plain_DATETIME_holding_the_utc_wall_clock()
    {
        if (!RequireServer()) return;
        FreshTable();
        await AsyncStore().CreateAsync(Row(), null, CancellationToken.None);

        var declared = Scalar("SELECT data_type FROM information_schema.columns "
                            + $"WHERE table_schema = DATABASE() AND table_name = '{TableName}' "
                            + "AND column_name = 'ObservedAt'");
        var stored = Scalar($"SELECT CAST(ObservedAt AS CHAR(40)) FROM `{TableName}` LIMIT 1");

        declared.Should().Be("datetime",
            "MySQL has no tz-aware type this framework maps to, so DbType.DateTimeOffset falls back to "
          + "DATETIME — the fallback half of the contract");
        stored.Should().StartWith("2026-03-15 10:30:00",
            "the offset is dropped and the UTC wall clock stored; the instant is recoverable only because "
          + "both sides agree the column holds UTC, which is what [UtcField] declares");
    }

    /// <summary>
    /// TASK-263 criterion 8, the delegated survey: does MySQL share the <c>Kind=Utc</c> inference asymmetry
    /// TASK-256 fixed on PostgreSQL? Measured answer: <b>no</b>.
    ///
    /// <para>
    /// On PostgreSQL a <c>Kind=Utc</c> <c>DateTime</c> bound with no <c>DbType</c> is inferred as
    /// <c>timestamptz</c> and cast into the timezone-less column through the session's zone — storing a shifted
    /// instant silently, which is the defect TASK-256 exists to remove. MySQL does not do this: there is no
    /// tz-aware type in play for <c>DbType.DateTime</c>, so the wall clock is stored as supplied whatever the
    /// session zone. TASK-256 therefore keeps its normalisation on <c>PostgreSQLConnector</c> alone, and this
    /// test is why that is a measurement rather than an assumption.
    /// </para>
    /// <para>
    /// This is the <b>plain</b> <c>DateTime</c> path deliberately — no <c>[UtcField]</c> — because that is the
    /// path the asymmetry would live on. SQL Server and SQLite are not tested for it: neither converts its
    /// datetime type on storage at all, so there is no session zone for a shift to come from.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_plain_utc_kinded_datetime_is_not_shifted_by_a_non_utc_session()
    {
        if (!RequireServer()) return;
        FreshTable();

        Exec("SET GLOBAL time_zone = '+05:30'");
        try
        {
            MySqlConnection.ClearAllPools();
            Scalar("SELECT @@session.time_zone").Should().Be("+05:30");

            // NoticeDate is an unmarked DateTime carrying Kind=Utc -- the exact shape that shifts on PostgreSQL.
            await AsyncStore().CreateAsync(Row(), null, CancellationToken.None);

            Scalar($"SELECT CAST(NoticeDate AS CHAR(40)) FROM `{TableName}` LIMIT 1")
                .Should().StartWith("2026-03-15 10:30:00",
                    "no shift on MySQL, so TASK-256's normalisation is correctly PostgreSQL-only. If this ever "
                  + "reads 15:30 or 16:00, MySQL has acquired the asymmetry and needs the same helper");
        }
        finally
        {
            try { Exec("SET GLOBAL time_zone = 'SYSTEM'"); MySqlConnection.ClearAllPools(); } catch { }
        }
    }

    /// <summary>
    /// A non-UTC session must change nothing — MySQL does not convert <c>DATETIME</c> on storage. Asserting it
    /// is what makes this a contract rather than a coincidence, and it is the mirror of the PostgreSQL suite's
    /// non-UTC test, which exists there because that provider <i>does</i> convert.
    /// </summary>
    [Fact]
    public async Task A_non_utc_session_does_not_shift_the_stored_instant()
    {
        if (!RequireServer()) return;
        FreshTable();

        Exec("SET GLOBAL time_zone = '+05:30'");
        try
        {
            MySqlConnection.ClearAllPools();
            Scalar("SELECT @@session.time_zone").Should().Be("+05:30",
                "if the session is not actually non-UTC this test cannot observe what it exists for");

            await AsyncStore().CreateAsync(Row(), null, CancellationToken.None);
            var read = (await AsyncStore().ReadAsync(CancellationToken.None)).Single();

            read.ObservedAt.Should().Be(Utc, "the instant must not depend on the server's time zone");
            Scalar($"SELECT CAST(ObservedAt AS CHAR(40)) FROM `{TableName}` LIMIT 1")
                .Should().StartWith("2026-03-15 10:30:00",
                    "and the stored wall clock is still the UTC one — unlike PostgreSQL's timestamptz, which "
                  + "renders in the session zone");
        }
        finally
        {
            try { Exec("SET GLOBAL time_zone = 'SYSTEM'"); MySqlConnection.ClearAllPools(); } catch { }
        }
    }
}
