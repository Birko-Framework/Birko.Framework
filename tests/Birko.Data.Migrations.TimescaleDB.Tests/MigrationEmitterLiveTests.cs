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
[Collection(TimescaleDbLiveCollection.Name)]
public class MigrationEmitterLiveTests : IDisposable
{
    private const string Table = "MigMetrics";
    private const string Aggregate = "MigDailyStats";
    private const string IntTable = "MigIntSeq";

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

    private static int ChunkCount(string table) => int.Parse(Scalar(
        $"SELECT COUNT(*) FROM timescaledb_information.chunks WHERE hypertable_name = '{table}'"));

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
        Exec($"DROP TABLE IF EXISTS \"{IntTable}\" CASCADE");
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
    /// TASK-261 — the chunk interval is read back. This assertion is the <b>inversion</b> of the pin this
    /// suite carried while the defect stood: <c>GetChunkInterval</c> asked for <c>chunk_time_interval</c> from
    /// <c>timescaledb_information.hypertables</c>, a column TimescaleDB removed from that view in <b>2.0</b>
    /// when it moved the value to <c>timescaledb_information.dimensions</c> and renamed it
    /// <c>time_interval</c>. It therefore raised <c>42703</c> on every 2.x server — all of them — and was not
    /// swallowed, so a migration calling it failed outright.
    /// <para>
    /// The <c>42703</c> assertion is <b>gone</b> rather than kept alongside this one: two tests asserting
    /// opposite things about one method is not extra coverage, it is a contradiction the next reader has to
    /// resolve.
    /// </para>
    /// </summary>
    [Fact]
    public void GetChunkInterval_returnsThePrimaryDimensionsInterval()
    {
        if (!RequireServer()) return;
        Reset();
        CreateBaseTable();

        var (connection, context) = NewContext();
        using (connection)
        {
            var probe = new Probe();
            probe.Hypertable(context, Table, "Ts", "3 days");

            probe.Chunk(context, Table).Should().Be("3 days",
                "the value declared at create_hypertable must round-trip, not merely be readable");
        }
    }

    /// <summary>
    /// A space-partitioned hypertable must still yield its <i>time</i> dimension's interval.
    /// <para>
    /// <b>What this does and does not witness.</b> Removing <c>dimension_number = 1</c> from the reader fails
    /// <b>no</b> test, including this one — measured. <c>timescaledb_information.dimensions</c> carries its own
    /// <c>ORDER BY</c>, so dimension 1 comes back first and an unrestricted <c>ExecuteScalar</c> happens to
    /// take the right row on 2.29.2. Said plainly rather than left implied, because a revert that fails
    /// nothing is otherwise indistinguishable from a redundant clause. The hazard the clause guards is pinned
    /// separately by <see cref="TheDimensionsViewReturnsANullIntervalRowForASpaceDimension"/>.
    /// </para>
    /// </summary>
    [Fact]
    public void GetChunkInterval_readsTheTimeDimensionOfASpacePartitionedHypertable()
    {
        if (!RequireServer()) return;
        Reset();
        CreateBaseTable();

        var (connection, context) = NewContext();
        using (connection)
        {
            var probe = new Probe();
            probe.HypertableWithSpace(context, Table, "Ts", "DeviceId", 4, "7 days");

            probe.Chunk(context, Table).Should().Be("7 days",
                "the time dimension's interval, not the space dimension's NULL");
        }
    }

    /// <summary>
    /// The catalogue fact that makes <c>dimension_number = 1</c> worth keeping: a space-partitioned
    /// hypertable has <b>2</b> dimension rows and only <b>1</b> of them carries an interval. So the reader's
    /// correctness without that clause rests entirely on the view's row order — which the query does not state
    /// and the catalogue does not promise.
    /// <para>
    /// Asserted against the server rather than through <c>GetChunkInterval</c>, because the reader cannot
    /// witness it: the order currently favours the right row. This is the test that fails if TimescaleDB ever
    /// changes that shape, which is exactly the kind of drift TASK-261 is fixing — the chunk interval moved
    /// view and changed name in 2.0.
    /// </para>
    /// </summary>
    [Fact]
    public void TheDimensionsViewReturnsANullIntervalRowForASpaceDimension()
    {
        if (!RequireServer()) return;
        Reset();
        CreateBaseTable();

        var (connection, context) = NewContext();
        using (connection)
        {
            new Probe().HypertableWithSpace(context, Table, "Ts", "DeviceId", 4, "7 days");
        }

        var rows = int.Parse(Scalar(
            "SELECT COUNT(*) FROM timescaledb_information.dimensions "
          + $"WHERE hypertable_name = '{Table}'"));
        var withInterval = int.Parse(Scalar(
            "SELECT COUNT(COALESCE(time_interval::text, integer_interval::text)) "
          + "FROM timescaledb_information.dimensions "
          + $"WHERE hypertable_name = '{Table}'"));

        rows.Should().Be(2, "one row per dimension");
        withInterval.Should().Be(1,
            "only the time dimension has an interval -- so an unrestricted ExecuteScalar is order-dependent");
    }

    /// <summary>
    /// An integer-partitioned hypertable keeps its width in <c>integer_interval</c> with <c>time_interval</c>
    /// NULL, so the reader coalesces rather than returning null — null would claim no interval is configured
    /// when one is. Note the discriminator is which column is populated: measured on 2.29.2 such a dimension
    /// still reports <c>dimension_type = 'Time'</c>, so branching on the type would be wrong.
    /// </summary>
    [Fact]
    public void GetChunkInterval_returnsTheIntegerIntervalWhenPartitionedOnAnInteger()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS \"{IntTable}\" CASCADE");
        Exec($"CREATE TABLE \"{IntTable}\" (Seq bigint NOT NULL, Value int)");
        Exec($"SELECT create_hypertable('\"{IntTable}\"', 'seq', chunk_time_interval => 100000)");

        var (connection, context) = NewContext();
        using (connection)
        {
            new Probe().Chunk(context, IntTable).Should().Be("100000",
                "the integer width is the chunk interval; returning null would say none is configured");
        }
    }

    /// <summary>A name that is not a hypertable has no interval, and that is a null rather than a throw.</summary>
    [Fact]
    public void GetChunkInterval_returnsNullForSomethingThatIsNotAHypertable()
    {
        if (!RequireServer()) return;
        Reset();
        CreateBaseTable();

        var (connection, context) = NewContext();
        using (connection)
        {
            new Probe().Chunk(context, Table).Should().BeNull(
                "the table exists but was never converted, so the dimensions view has no row for it");
        }
    }

    // ================================================================ continuous aggregate

    /// <summary>
    /// The aggregate's view name and source table are real identifier positions, so they are quoted without
    /// literal escaping.
    /// <para>
    /// <b>Kept as a pin after TASK-255, with the column now passed explicitly.</b> Before that task the
    /// bucketing column was the hardcoded literal <c>time</c>, so a table with such a column was the only
    /// shape that could work at all; now it is simply one legal argument among others. This test proves that
    /// shape did not regress — a hand-made table whose column really is named <c>time</c> still works.
    /// </para>
    /// </summary>
    [Fact]
    public void ContinuousAggregate_isCreatedForPascalCaseNames()
    {
        if (!RequireServer()) return;
        Reset();
        // A column literally named "time": legal, conventional in TimescaleDB, and no longer the only
        // shape that works (TASK-255).
        Exec($"CREATE TABLE \"{Table}\" (time timestamptz NOT NULL, Value double precision)");

        var (connection, context) = NewContext();
        using (connection)
        {
            var probe = new Probe();
            probe.Hypertable(context, Table, "time", "1 day");
            var sql = TimescaleDBMigration.BuildContinuousAggregateSql(
                new TimescaleDBConnector(Settings()), Aggregate, Table, "1 day", "time", new[] { ContinuousAggregateProjection.Of("avg", "Value", "avg_value") });
            Exec(sql);
        }

        Scalar($"SELECT COUNT(*) FROM timescaledb_information.continuous_aggregates "
             + $"WHERE view_name = '{Aggregate}'").Should().Be("1");
    }

    /// <summary>
    /// <b>TASK-255, inverted.</b> This test previously asserted that the aggregate <i>could not be built</i>
    /// for a normally-named time column — <c>PostgresException</c> / <c>42703</c>, because
    /// <c>time_bucket</c> was handed a hardcoded <c>time</c> that no framework-created table has. It was the
    /// defect pin, and it said in its own summary that the day TASK-255 landed it was what would change.
    /// <para>
    /// It asserts the <b>catalogue row</b>, not merely that the DDL did not throw: this layer swallows, and
    /// TASK-209 records a case where a regression test passed against unfixed code for exactly that reason.
    /// </para>
    /// <para>
    /// <b>This is also what witnesses BARE rather than quoted.</b> <c>CreateBaseTable</c> emits
    /// <c>(Ts timestamptz …)</c> with a bare column inside a quoted table, so PostgreSQL stores <c>ts</c>;
    /// a quoted <c>"Ts"</c> would raise <c>42703</c> here. The offline test pins the rendering, this pins
    /// that the rendering resolves.
    /// </para>
    /// </summary>
    [Fact]
    public void ContinuousAggregate_isCreatedWhenTheTimeColumnIsNotNamedTime()
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
            new TimescaleDBConnector(Settings()), Aggregate, Table, "1 day", "Ts", new[] { ContinuousAggregateProjection.Of("avg", "Value", "avg_value") });

        Exec(sql);

        Scalar($"SELECT COUNT(*) FROM timescaledb_information.continuous_aggregates "
             + $"WHERE view_name = '{Aggregate}'").Should().Be("1",
                "the bucketing column is now a parameter, so a PascalCase entity's time column reaches "
              + "time_bucket bare and resolves against the folded column CREATE TABLE actually created "
              + "(TASK-255)");
    }

    /// <summary>
    /// <b>The second half of the proof: the aggregate must AGGREGATE, not merely appear in a catalogue.</b>
    /// The sibling above asserts the row exists; a materialized view can exist and still bucket nothing.
    /// This inserts rows spanning two days, refreshes, and reads the buckets back — so a fix that created a
    /// view over the wrong column, or over no rows, fails here rather than passing quietly.
    /// <para>
    /// Modelled on <c>CreateHypertable_routesRowsIntoSeparateChunks</c>, which exists for the same reason:
    /// the failures this family produces are silent, so the test has to demand something a broken
    /// implementation cannot fake.
    /// </para>
    /// </summary>
    [Fact]
    public void ContinuousAggregate_bucketsRowsItCanBeReadBackFrom()
    {
        if (!RequireServer()) return;
        Reset();
        CreateBaseTable();

        var (connection, context) = NewContext();
        using (connection)
        {
            new Probe().Hypertable(context, Table, "Ts", "1 day");
        }

        Exec($"INSERT INTO \"{Table}\" (Ts, DeviceId, Value) VALUES "
           + "('2026-01-01T01:00:00Z', 1, 10), "
           + "('2026-01-01T02:00:00Z', 1, 20), "
           + "('2026-01-02T01:00:00Z', 1, 90)");

        Exec(TimescaleDBMigration.BuildContinuousAggregateSql(
            new TimescaleDBConnector(Settings()), Aggregate, Table, "1 day", "Ts", new[] { ContinuousAggregateProjection.Of("avg", "Value", "avg_value") }));

        Exec(TimescaleDBMigration.BuildRefreshContinuousAggregateSql(
            new TimescaleDBConnector(Settings()), Aggregate));

        Scalar($"SELECT COUNT(*) FROM \"{Aggregate}\"").Should().Be("2",
            "three rows spanning two days must bucket into two daily buckets");

        Scalar($"SELECT avg_value FROM \"{Aggregate}\" ORDER BY bucket").Should().Be("15",
            "the first day's two values (10, 20) average to 15 — proving the aggregate reads the column "
          + "the caller named, not some other one");
    }

    // ================================================================ chunk routing

    /// <summary>
    /// <b>The proof that the hypertable is doing a hypertable's job, not merely appearing in a catalogue
    /// view.</b> TASK-253's human test plan is <c>N/A</c> on the grounds that the evidence is "a row in
    /// <c>timescaledb_information.hypertables</c> and a chunk count &gt; 1" — the row was asserted by the
    /// tests above, and this is the second half, added at the close gate rather than left as a claim.
    /// <para>
    /// It matters because the two failures this task fixed were both <i>silent</i>: a bare regclass produced no
    /// hypertable while a plain PostgreSQL table served reads and writes perfectly well. A plain table accepts
    /// these three inserts and reports one "chunk" count of zero — so partitioning actually happening is the
    /// thing a plain table cannot fake.
    /// </para>
    /// </summary>
    [Fact]
    public void CreateHypertable_routesRowsIntoSeparateChunks()
    {
        if (!RequireServer()) return;
        Reset();
        CreateBaseTable();

        var (connection, context) = NewContext();
        using (connection)
        {
            new Probe().Hypertable(context, Table, "Ts", "1 day");
        }

        IsHypertable(Table).Should().BeTrue("the premise — everything below is vacuous over a plain table");
        ChunkCount(Table).Should().Be(0, "no rows yet, so no chunks");

        // Three rows a month apart, against a one-day chunk interval: one chunk each.
        Exec($"INSERT INTO \"{Table}\" (Ts, DeviceId, Value) VALUES "
           + "('2026-01-15T00:00:00Z', 1, 1.0), "
           + "('2026-02-15T00:00:00Z', 2, 2.0), "
           + "('2026-03-15T00:00:00Z', 3, 3.0)");

        ChunkCount(Table).Should().Be(3,
            "chunk routing is what a hypertable adds over a plain table, and it is exactly what was silently "
          + "absent for every PascalCase entity before this fix");
        Scalar($"SELECT COUNT(*) FROM \"{Table}\"").Should().Be("3", "and the rows are still readable");
    }

    // ================================================================ containment, against the server

    /// <summary>
    /// <b>The escaping proved by the parser rather than by a string assertion.</b> The offline suite asserts a
    /// payload comes back in its escaped form; only the server says whether PostgreSQL then reads the statement
    /// as <i>one</i> statement. Added at TASK-253's close gate during the security pass.
    /// <para>
    /// <b>The payload has to leave a VALID leading statement, and getting that wrong is how this test nearly
    /// shipped useless.</b> Its first draft attacked the <i>table</i> argument of <c>create_hypertable</c>, so
    /// the injected batch read
    /// <c>create_hypertable('Rank"'); CREATE TABLE "Pwned" …</c> — whose first statement fails on an absurd
    /// relation name. Npgsql sends a batch as one command and PostgreSQL aborts the whole batch on the first
    /// error, so the appended statement never ran and the test <b>passed over unescaped code</b>. Measured, not
    /// theorised: with escaping removed it reported green.
    /// </para>
    /// <para>
    /// So the payload goes into the <c>INTERVAL</c> argument of <c>add_retention_policy</c> against the real
    /// hypertable, leaving <c>add_retention_policy('"MigMetrics"', INTERVAL '30 days')</c> — valid — before the
    /// <c>;</c>. That is the shape a real attacker would pick, and it is the shape that discriminates.
    /// </para>
    /// </summary>
    [Fact]
    public void AnEmitterContainsAPayloadThatLeavesAValidLeadingStatement()
    {
        if (!RequireServer()) return;
        Reset();
        Exec("DROP TABLE IF EXISTS \"Pwned\" CASCADE");
        CreateBaseTable();

        var (connection, context) = NewContext();
        using (connection)
        {
            var probe = new Probe();
            probe.Hypertable(context, Table, "Ts", "1 day");
            IsHypertable(Table).Should().BeTrue("the premise: the leading statement must be able to succeed");

            // Completes a valid INTERVAL, closes the literal, appends a statement, comments out the tail.
            const string payload = "30 days'); CREATE TABLE \"Pwned\" (x INTEGER); --";

            Attempt(() => probe.Retention(context, Table, payload));
        }

        Scalar("SELECT to_regclass('\"Pwned\"') IS NULL").Should().Be("True",
            "the payload is data inside a literal. If Pwned exists, the interval argument let a caller append a "
          + "statement after a statement that succeeded — which is what this emitter did before TASK-253, with "
          + "no escaping whatsoever");
    }

    /// <summary>
    /// Breadth over every emitter and every caller-supplied argument. Each payload here makes its own leading
    /// statement invalid, so PostgreSQL aborts the batch regardless of escaping — which is precisely why this
    /// test <b>cannot stand alone</b> and why the one above exists. Kept because it exercises all nine emitters
    /// against a real parser and would catch a malformed statement, a wrong argument position, or an escape
    /// that corrupts a name; it is breadth, not the containment proof.
    /// </summary>
    [Fact]
    public void EveryEmitter_survivesAPayloadInEveryArgument()
    {
        if (!RequireServer()) return;
        Reset();
        Exec("DROP TABLE IF EXISTS \"Pwned\" CASCADE");
        CreateBaseTable();

        var (connection, context) = NewContext();
        using (connection)
        {
            var probe = new Probe();
            probe.Hypertable(context, Table, "Ts", "1 day");

            const string payload = "Rank\"'); CREATE TABLE \"Pwned\" (x INTEGER); --";

            Attempt(() => probe.Hypertable(context, payload, "Ts"));
            Attempt(() => probe.Hypertable(context, Table, payload));
            Attempt(() => probe.Hypertable(context, Table, "Ts", payload));
            Attempt(() => probe.HypertableWithSpace(context, payload, "Ts", "DeviceId", 4));
            Attempt(() => probe.HypertableWithSpace(context, Table, "Ts", payload, 4));
            Attempt(() => probe.Compression(context, payload, "7 days", "ts"));
            Attempt(() => probe.Compression(context, Table, "7 days", payload));
            Attempt(() => probe.Compression(context, Table, payload, "ts"));
            Attempt(() => probe.Retention(context, payload, "30 days"));
            Attempt(() => probe.DropCompression(context, payload));
            Attempt(() => probe.DropRetention(context, payload));
        }

        Scalar("SELECT to_regclass('\"Pwned\"') IS NULL").Should().Be("True");
    }

    /// <summary>
    /// Runs an emitter that is <b>expected</b> to fail and swallows only the database's complaint — a payload
    /// naming no real relation must produce an error. Which error does not matter here; what matters is that no
    /// <c>Pwned</c> table exists afterwards. A non-database exception still escapes.
    /// </summary>
    private static void Attempt(Action emit)
    {
        try
        {
            emit();
        }
        catch (PostgresException)
        {
            // Expected: the payload is a table, column or interval that does not parse or does not exist.
        }
    }
}
