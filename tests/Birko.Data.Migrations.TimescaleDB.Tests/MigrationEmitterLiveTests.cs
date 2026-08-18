using System;
using Birko.Data.Migrations.Context;
using Birko.Data.Migrations.TimescaleDB;
using Birko.Data.Migrations.TimescaleDB.Context;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.TimescaleDB.Stores;
using FluentAssertions;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.Migrations.TimescaleDB.Tests;

/// <summary>
/// TASK-253 — the migration emitters against a live TimescaleDB.
///
/// <para>
/// <b>Why offline string assertions are not enough here.</b> The offline suites pin what the builders
/// <i>compose</i>; only a server says whether the composition is something TimescaleDB accepts. And this layer
/// is exactly where "it did not throw" means nothing: TASK-209 established that a swallowing DDL path makes a
/// no-op indistinguishable from success, and TASK-472 measured the specific case — a bare regclass raises
/// <c>42P01</c>, which <c>IsMissingTableException</c> classifies as a missing table, so the handler swallows it
/// and reports success while creating no hypertable at all. So every assertion below reads the
/// <c>timescaledb_information</c> catalogue or counts rows.
/// </para>
///
/// <para>
/// Gated on <c>BIRKO_TS_HOST</c> (+ <c>_PORT</c> / <c>_USER</c> / <c>_PASSWORD</c> / <c>_DB</c>), matching the
/// shape used by <c>Birko.Data.TimescaleDB.Tests</c>; a skipped run says so out loud, and
/// <c>BIRKO_REQUIRE_LIVE</c> turns absence into a failure.
/// </para>
///
/// <para>
/// Verified against <b>TimescaleDB 2.29.2 / PostgreSQL 16</b>. Worth recording: the positional
/// <c>create_hypertable(relation, time_column_name, partitioning_column, number_partitions, ...)</c> overload
/// is <i>still present</i> on 2.29 alongside the newer dimension-builder form, so the space-partitioning
/// emitter works. That was flagged as a risk before measuring (the positional form has been described as
/// deprecated since 2.13) and the measurement says it has not been removed.
/// </para>
/// </summary>
public class MigrationEmitterLiveTests : IDisposable
{
    private const string Table = "MigMetrics";
    private const string Aggregate = "MigDailyStats";

    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_TS_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_TS_PORT"), out var p) ? p : 5432;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_TS_USER") ?? "postgres";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_TS_PASSWORD") ?? "postgres";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_TS_DB") ?? "birkoview";
    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _output;

    public MigrationEmitterLiveTests(ITestOutputHelper output) => _output = output;

    private bool RequireServer()
    {
        if (!string.IsNullOrWhiteSpace(Host))
        {
            return true;
        }
        const string message = "SKIPPED: no live TimescaleDB. Set BIRKO_TS_HOST to exercise this test; "
                             + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (RequireLive)
        {
            throw new InvalidOperationException(message);
        }
        return false;
    }

    private static TimescaleDBSettings Settings()
        => new(Host!, Database, User, Password, Port, "ts", "1 day");

    private static void Exec(string sql)
    {
        using var conn = new NpgsqlConnection(Settings().GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static string Scalar(string sql)
    {
        using var conn = new NpgsqlConnection(Settings().GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToString(cmd.ExecuteScalar()) ?? "<null>";
    }

    /// <summary>
    /// The name is compared with its <b>case intact</b>: the catalogue stores what
    /// <c>CREATE TABLE "MigMetrics"</c> created. Lower-casing it here is the false negative that briefly made
    /// TASK-472's working fix look broken.
    /// </summary>
    private static bool IsHypertable(string table) => Scalar(
        $"SELECT COUNT(*) FROM timescaledb_information.hypertables WHERE hypertable_name = '{table}'") == "1";

    private static int PolicyCount(string proc, string table) => int.Parse(Scalar(
        $"SELECT COUNT(*) FROM timescaledb_information.jobs WHERE proc_name = '{proc}' AND hypertable_name = '{table}'"));

    /// <summary>
    /// A real migration context over a real connection, exactly as <c>TimescaleDBMigrationRunner</c> builds
    /// one — that is what carries the connector the emitters resolve their identifiers through.
    /// </summary>
    private static (NpgsqlConnection connection, IMigrationContext context) NewContext()
    {
        var connection = new NpgsqlConnection(Settings().GetConnectionString());
        connection.Open();
        return (connection, new TimescaleDBMigrationContext(connection, null, new TimescaleDBConnector(Settings())));
    }

    /// <summary>Exposes the protected emitters, which is all a concrete migration is needed for here.</summary>
    private sealed class Probe : TimescaleDBMigration
    {
        public override long Version => 1;
        public override string Name => nameof(Probe);
        public override string Description => "TASK-253 live probe";
        public override DateTime CreatedAt => new(2026, 8, 18, 0, 0, 0, DateTimeKind.Utc);
        public override void Up(IMigrationContext context) { }
        public override void Down(IMigrationContext context) { }

        public void Hypertable(IMigrationContext c, string t, string col, string? chunk = null) => CreateHypertable(c, t, col, chunk);
        public void HypertableWithSpace(IMigrationContext c, string t, string col, string space, int n, string? chunk = null)
            => CreateHypertableWithSpace(c, t, col, space, n, chunk);
        public void Compression(IMigrationContext c, string t, string after, string orderBy) => AddCompressionPolicy(c, t, after, orderBy);
        public void Retention(IMigrationContext c, string t, string after) => AddRetentionPolicy(c, t, after);
        public void DropCompression(IMigrationContext c, string t) => RemoveCompressionPolicy(c, t);
        public void DropRetention(IMigrationContext c, string t) => RemoveRetentionPolicy(c, t);
        public bool Hyper(IMigrationContext c, string t) => IsHypertable(c, t);
        public string? Chunk(IMigrationContext c, string t) => GetChunkInterval(c, t);
    }

    private static void CreateBaseTable()
        => Exec($"CREATE TABLE \"{Table}\" (Ts timestamptz NOT NULL, DeviceId int NOT NULL, Value double precision)");

    private void Reset()
    {
        Exec($"DROP MATERIALIZED VIEW IF EXISTS \"{Aggregate}\" CASCADE");
        Exec($"DROP TABLE IF EXISTS \"{Table}\" CASCADE");
    }

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(Host)) return;
        try { Reset(); } catch { }
    }

    // ================================================================ create_hypertable

    /// <summary>
    /// <b>The load-bearing test.</b> A PascalCase table plus a PascalCase-declared time column — the shape
    /// every Birko entity has, and the one that produced nothing at all before this fix.
    /// </summary>
    [Fact]
    public void CreateHypertable_convertsAPascalCaseTable()
    {
        if (!RequireServer()) return;
        Reset();
        CreateBaseTable();

        var (connection, context) = NewContext();
        using (connection)
        {
            new Probe().Hypertable(context, Table, "Ts", "1 day");
        }

        IsHypertable(Table).Should().BeTrue(
            "the regclass must reach the table it names. Bare, it folded to 'migmetrics', raised 42P01, and "
          + "the handler swallowed it as a missing table — reporting success over a plain PostgreSQL table");
    }

    /// <summary>
    /// The column half in isolation. <c>Ts</c> is declared PascalCase and stored folded, because
    /// <c>CREATE TABLE</c> emits column definitions bare — so an unfolded <c>name</c> argument raises
    /// <c>42703</c>. Unlike the table half this one is <i>loud</i>, which is why it was never the hidden defect.
    /// </summary>
    [Fact]
    public void CreateHypertable_matchesTheFoldedStoredColumn()
    {
        if (!RequireServer()) return;
        Reset();
        CreateBaseTable();

        Scalar($"SELECT string_agg(column_name, ',' ORDER BY column_name) FROM information_schema.columns "
             + $"WHERE table_name = '{Table}'")
            .Should().Contain("ts").And.NotContain("Ts");

        var (connection, context) = NewContext();
        using (connection)
        {
            new Probe().Hypertable(context, Table, "Ts");
        }

        IsHypertable(Table).Should().BeTrue();
    }

    [Fact]
    public void CreateHypertable_appliesTheChunkInterval()
    {
        if (!RequireServer()) return;
        Reset();
        CreateBaseTable();

        var (connection, context) = NewContext();
        using (connection)
        {
            new Probe().Hypertable(context, Table, "Ts", "2 days");
        }

        // The column is `time_interval` on the `dimensions` view. TimescaleDB 2.x moved the chunk interval
        // off `hypertables` and renamed it; `chunk_time_interval` no longer exists anywhere in the catalogue,
        // which is what TimescaleDBMigration.GetChunkInterval still asks for — see the TASK-261 pin below.
        Scalar($"SELECT time_interval::text FROM timescaledb_information.dimensions "
             + $"WHERE hypertable_name = '{Table}' AND dimension_number = 1").Should().Be("2 days");
    }

    /// <summary>
    /// Space partitioning: both column arguments are <c>name</c>s and both must be folded. Also the test that
    /// establishes the positional overload still exists on this server — see the class remarks.
    /// </summary>
    [Fact]
    public void CreateHypertableWithSpace_foldsBothColumns()
    {
        if (!RequireServer()) return;
        Reset();
        CreateBaseTable();

        var (connection, context) = NewContext();
        using (connection)
        {
            new Probe().HypertableWithSpace(context, Table, "Ts", "DeviceId", 4, "1 day");
        }

        IsHypertable(Table).Should().BeTrue();
        Scalar($"SELECT COUNT(*) FROM timescaledb_information.dimensions WHERE hypertable_name = '{Table}'")
            .Should().Be("2", "a time dimension and a space dimension");
    }

    // ================================================================ policies

    /// <summary>
    /// The compression policy is where the table needs <b>two different treatments in one statement</b> —
    /// <c>ALTER TABLE "MigMetrics"</c> and <c>add_compression_policy('"MigMetrics"')</c>. Getting either wrong
    /// fails the whole script, so a policy row in the catalogue proves both.
    /// </summary>
    [Fact]
    public void CompressionPolicy_isRegisteredForAPascalCaseTable()
    {
        if (!RequireServer()) return;
        Reset();
        CreateBaseTable();

        var (connection, context) = NewContext();
        using (connection)
        {
            var probe = new Probe();
            probe.Hypertable(context, Table, "Ts", "1 day");
            probe.Compression(context, Table, "7 days", "ts");
        }

        PolicyCount("policy_compression", Table).Should().Be(1);
    }

    [Fact]
    public void RetentionPolicy_isRegisteredAndCanBeRemoved()
    {
        if (!RequireServer()) return;
        Reset();
        CreateBaseTable();

        var (connection, context) = NewContext();
        using (connection)
        {
            var probe = new Probe();
            probe.Hypertable(context, Table, "Ts", "1 day");
            probe.Retention(context, Table, "30 days");

            PolicyCount("policy_retention", Table).Should().Be(1);

            probe.DropRetention(context, Table);
        }

        PolicyCount("policy_retention", Table).Should().Be(0,
            "remove_retention_policy takes a regclass too, so it fails on a PascalCase table just as the add did");
    }

    [Fact]
    public void CompressionPolicy_canBeRemoved()
    {
        if (!RequireServer()) return;
        Reset();
        CreateBaseTable();

        var (connection, context) = NewContext();
        using (connection)
        {
            var probe = new Probe();
            probe.Hypertable(context, Table, "Ts", "1 day");
            probe.Compression(context, Table, "7 days", "ts");
            probe.DropCompression(context, Table);
        }

        PolicyCount("policy_compression", Table).Should().Be(0);
    }

    // ================================================================ the case-intact readers

    /// <summary>
    /// <c>IsHypertable</c> parameterises and passes the name through <b>unfolded</b>, because the catalogue
    /// keeps its case. This pins that, so nobody "fixes" it for symmetry with the emitters — which fold their
    /// <i>column</i> for a completely different reason. Both directions are asserted: the exact name is found
    /// and the folded one is not.
    /// </summary>
    [Fact]
    public void TheCatalogueReaders_findAPascalCaseHypertable()
    {
        if (!RequireServer()) return;
        Reset();
        CreateBaseTable();

        var (connection, context) = NewContext();
        using (connection)
        {
            var probe = new Probe();
            probe.Hypertable(context, Table, "Ts", "3 days");

            probe.Hyper(context, Table).Should().BeTrue("the catalogue stores 'MigMetrics', not 'migmetrics'");
            probe.Hyper(context, Table.ToLowerInvariant()).Should().BeFalse(
                "and folding the name here would therefore find nothing — the false negative TASK-472 hit");
        }
    }

    /// <summary>
    /// <b>A defect this suite found rather than fixed, pinned as current behaviour: TASK-261.</b>
    /// <c>GetChunkInterval</c> asks for <c>chunk_time_interval</c> from
    /// <c>timescaledb_information.hypertables</c> — a column TimescaleDB removed from that view in <b>2.0</b>,
    /// moving the value to <c>timescaledb_information.dimensions</c> and renaming it <c>time_interval</c>. So
    /// the method raises <c>42703</c> on every TimescaleDB 2.x server, which is all of them; measured here on
    /// 2.29.2.
    /// <para>
    /// Out of scope for TASK-253, which is about identifier quoting and folding — this is catalogue drift, a
    /// different defect with a different fix. It is latent: a sweep found no consumer calling it. Pinned so the
    /// fix has a failing test to turn green, and so nobody reads the surrounding green suite as evidence that
    /// this method works.
    /// </para>
    /// </summary>
    [Fact]
    public void GetChunkInterval_readsAColumnTimescaleDB2Removed_TASK261()
    {
        if (!RequireServer()) return;
        Reset();
        CreateBaseTable();

        var (connection, context) = NewContext();
        using (connection)
        {
            var probe = new Probe();
            probe.Hypertable(context, Table, "Ts", "3 days");

            var act = () => probe.Chunk(context, Table);

            act.Should().Throw<PostgresException>()
                .Which.SqlState.Should().Be("42703",
                    "chunk_time_interval was moved to timescaledb_information.dimensions and renamed "
                  + "time_interval in TimescaleDB 2.0, so this reader has been broken for the whole 2.x line");
        }
    }

    // ================================================================ continuous aggregate

    /// <summary>
    /// The aggregate's view name and source table are real identifier positions, so they are quoted without
    /// literal escaping. The bucketing column is still the hardcoded <c>time</c> (TASK-255), so the source
    /// table needs a column of that name for this to work at all — which is precisely the defect TASK-255
    /// records, demonstrated here rather than asserted in prose.
    /// </summary>
    [Fact]
    public void ContinuousAggregate_isCreatedForPascalCaseNames_onlyWithATimeColumn()
    {
        if (!RequireServer()) return;
        Reset();
        // Note the column literally named "time": TASK-255's hardcoding means nothing else can work.
        Exec($"CREATE TABLE \"{Table}\" (time timestamptz NOT NULL, Value double precision)");

        var (connection, context) = NewContext();
        using (connection)
        {
            var probe = new Probe();
            probe.Hypertable(context, Table, "time", "1 day");
            var sql = TimescaleDBMigration.BuildContinuousAggregateSql(
                new TimescaleDBConnector(Settings()), Aggregate, Table, "1 day", "avg(Value) AS avg_value");
            Exec(sql);
        }

        Scalar($"SELECT COUNT(*) FROM timescaledb_information.continuous_aggregates "
             + $"WHERE view_name = '{Aggregate}'").Should().Be("1");
    }

    /// <summary>
    /// The TASK-255 defect, measured rather than described: with a normally-named time column the aggregate
    /// cannot be built at all, because <c>time_bucket</c> is handed a column that does not exist. Pinned as
    /// <i>current</i> behaviour so the day TASK-255 lands, this test is what changes.
    /// </summary>
    [Fact]
    public void ContinuousAggregate_cannotBeBuiltWhenTheTimeColumnIsNotNamedTime_TASK255()
    {
        if (!RequireServer()) return;
        Reset();
        CreateBaseTable();

        var (connection, context) = NewContext();
        using (connection)
        {
            new Probe().Hypertable(context, Table, "Ts", "1 day");
        }

        var sql = TimescaleDBMigration.BuildContinuousAggregateSql(
            new TimescaleDBConnector(Settings()), Aggregate, Table, "1 day", "avg(Value) AS avg_value");

        var act = () => Exec(sql);

        act.Should().Throw<PostgresException>()
            .Which.SqlState.Should().Be("42703",
                "column \"time\" does not exist — no framework-created table has one, because column "
              + "definitions are emitted bare and every Birko entity is PascalCase (TASK-255)");
    }
}
