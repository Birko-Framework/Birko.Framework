using System;
using Birko.Data.Migrations;
using Birko.Data.Migrations.Context;
using Birko.Data.Migrations.SQL.Settings;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.TimescaleDB.Stores;
using FluentAssertions;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.Migrations.TimescaleDB.Tests;

/// <summary>
/// TASK-281 — the continuous-aggregate emitters exercised **through `TimescaleDBMigrationRunner`**, which is
/// the only path a real migration takes and the one nothing in this tree covered.
///
/// <para>
/// <b>The coverage shape this file exists to close.</b> Every other test of these emitters — including the
/// live ones TASK-255 added — calls <c>Exec(BuildContinuousAggregateSql(...))</c> on a fresh connection, i.e.
/// in autocommit. So the suite tested the <i>SQL</i> thoroughly and the <i>execution model</i> not at all.
/// Compare TASK-246, where a feature worked in the branch nobody used and failed in the branch everybody
/// used, and a green suite said nothing.
/// </para>
///
/// <para>
/// <b>Measured on live TimescaleDB 2.29.2 / PostgreSQL 16.15 before a line of the fix was written</b>, because
/// the finding that produced this task came from a reviewer that had already been wrong once in the same pass:
/// <list type="bullet">
/// <item><c>CREATE MATERIALIZED VIEW … WITH (timescaledb.continuous)</c> inside a transaction → <b>25001</b>
/// <i>"CREATE MATERIALIZED VIEW ... WITH DATA cannot run inside a transaction block"</i>.</item>
/// <item>the same statement with <c>WITH NO DATA</c> → legal, survives the commit, view empty.</item>
/// <item><c>CALL refresh_continuous_aggregate(…)</c> inside a transaction → <b>25001</b>; in autocommit →
/// legal, populates.</item>
/// <item><c>add_continuous_aggregate_policy(…)</c> inside a transaction → <b>legal</b>, job survives the
/// commit. This is the one the original plan missed, and it is why a transactional migration is merely
/// <i>limited</i> rather than unable to populate an aggregate at all.</item>
/// <item><c>create_hypertable(…)</c> inside a transaction → legal. Recorded because it is this fixture's
/// precondition: it guarantees a failure here lands on the aggregate statement rather than on an earlier one
/// (§ TASK-259, where a mismatched probe entity produced an error indistinguishable from the bug).</item>
/// </list>
/// </para>
/// </summary>
[Collection(TimescaleDbLiveCollection.Name)]
public class ContinuousAggregateRunnerPathTests : IDisposable
{
    private const string Table = "RunMetrics";
    private const string Aggregate = "RunDailyStats";

    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_TS_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_TS_PORT"), out var p) ? p : 5432;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_TS_USER") ?? "postgres";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_TS_PASSWORD") ?? "postgres";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_TS_DB") ?? "birkoview";
    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _output;

    public ContinuousAggregateRunnerPathTests(ITestOutputHelper output) => _output = output;

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

    private void Reset()
    {
        Exec($"DROP MATERIALIZED VIEW IF EXISTS \"{Aggregate}\" CASCADE");
        Exec($"DROP TABLE IF EXISTS \"{Table}\" CASCADE");
        // The migration version table is "__Migrations" (SqlMigrationSettings.FullTableName). Dropping the
        // WRONG name here made version 1 persist between runs, so Migrate() found nothing to do and
        // reported success having created nothing — a false green in one ordering and a false red in
        // another (§ TASK-259: a fixture fault is indistinguishable from the defect).
        Exec("DROP TABLE IF EXISTS \"__Migrations\" CASCADE");
    }

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(Host)) return;
        try { Reset(); } catch { }
    }

    /// <summary>
    /// Creates the hypertable and then the aggregate, in one migration — the shape a real caller writes.
    /// <c>create_hypertable</c> is transaction-safe (measured), so if this migration fails inside a
    /// transaction it fails on the aggregate.
    /// </summary>
    private sealed class AggregateMigration : TimescaleDBMigration
    {
        public override long Version => 1;
        public override string Name => "create the aggregate";
        public override string Description => "TASK-281 runner path";
        public override DateTime CreatedAt => new(2026, 8, 24, 0, 0, 0, DateTimeKind.Utc);

        public override void Up(IMigrationContext context)
        {
            ExecuteRaw(context, $"CREATE TABLE IF NOT EXISTS \"{Table}\" (Ts timestamptz NOT NULL, Value double precision)");
            CreateHypertable(context, Table, "Ts", "1 day");
            CreateContinuousAggregate(context, Aggregate, Table, "1 day", "Ts", new[] { ContinuousAggregateProjection.Of("avg", "Value", "avg_value") });
        }

        public override void Down(IMigrationContext context)
        {
        }

        private static void ExecuteRaw(IMigrationContext context, string sql)
        {
            var sqlContext = (Birko.Data.Migrations.SQL.Context.SqlMigrationContext)context;
            using var command = sqlContext.Connection.CreateCommand();
            command.Transaction = sqlContext.Transaction;
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// <b>The prover.</b> Red before TASK-281 with <c>25001</c>, green after. Written and watched fail before
    /// any production line changed, because a fix measured against a test that was never red proves nothing.
    /// </summary>
    [Fact]
    public void A_migration_can_create_a_continuous_aggregate_under_the_default_transactional_runner()
    {
        if (!RequireServer()) return;
        Reset();

        var connector = new TimescaleDBConnector(Settings());
        var runner = new TimescaleDBMigrationRunner(connector);   // default UseTransaction = true
        runner.RegisterMigrations(new AggregateMigration());
        runner.Initialize();

        var result = runner.Migrate();

        result.Success.Should().BeTrue(
            "CREATE MATERIALIZED VIEW ... WITH (timescaledb.continuous) raises 25001 inside a transaction "
          + "block, and the runner opens one by default — so this is the only path a real migration takes "
          + "and it could not work (TASK-281)");

        Scalar($"SELECT COUNT(*) FROM timescaledb_information.continuous_aggregates WHERE view_name = '{Aggregate}'")
            .Should().Be("1", "assert the catalogue row, not merely that nothing threw (TASK-209)");
    }

    /// <summary>
    /// The cost of <c>WITH NO DATA</c>, asserted so nobody "fixes" the emptiness by removing it and silently
    /// reintroducing <c>25001</c>. The view exists and is empty until a refresh policy or an explicit refresh
    /// populates it.
    /// </summary>
    [Fact]
    public void The_aggregate_is_empty_until_it_is_populated()
    {
        if (!RequireServer()) return;
        Reset();

        var connector = new TimescaleDBConnector(Settings());
        var runner = new TimescaleDBMigrationRunner(connector);
        runner.RegisterMigrations(new AggregateMigration());
        runner.Initialize();
        runner.Migrate().Success.Should().BeTrue();

        Exec($"INSERT INTO \"{Table}\" (Ts, Value) VALUES ('2026-01-01T01:00:00Z', 10), ('2026-01-02T01:00:00Z', 20)");

        Scalar($"SELECT COUNT(*) FROM \"{Aggregate}\"").Should().Be("0",
            "WITH NO DATA is what makes the CREATE legal inside the runner's transaction; the view is empty "
          + "until populated, and that is the stated cost of TASK-281's option A");
    }

    /// <summary>Creates the aggregate, inserts rows, then asks for an immediate backfill.</summary>
    private sealed class CreateThenRefreshMigration : TimescaleDBMigration
    {
        public override long Version => 1;
        public override string Name => "create and backfill";
        public override string Description => "TASK-281 refresh path";
        public override DateTime CreatedAt => new(2026, 8, 24, 0, 0, 0, DateTimeKind.Utc);

        public override void Up(IMigrationContext context)
        {
            Raw(context, $"CREATE TABLE IF NOT EXISTS \"{Table}\" (Ts timestamptz NOT NULL, Value double precision)");
            CreateHypertable(context, Table, "Ts", "1 day");
            CreateContinuousAggregate(context, Aggregate, Table, "1 day", "Ts", new[] { ContinuousAggregateProjection.Of("avg", "Value", "avg_value") });
            Raw(context, $"INSERT INTO \"{Table}\" (Ts, Value) VALUES "
                       + "('2026-01-01T01:00:00Z', 10), ('2026-01-01T02:00:00Z', 20), ('2026-01-02T01:00:00Z', 90)");
            RefreshContinuousAggregate(context, Aggregate);
        }

        public override void Down(IMigrationContext context) { }

        private static void Raw(IMigrationContext context, string sql)
        {
            var c = (Birko.Data.Migrations.SQL.Context.SqlMigrationContext)context;
            using var command = c.Connection.CreateCommand();
            command.Transaction = c.Transaction;
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// The refusal. <c>refresh_continuous_aggregate()</c> raises <c>25001</c> in a transaction, so the
    /// framework refuses up front — <b>not</b> because the server's message is unclear (it is excellent),
    /// but because only the framework can route the caller to <c>UseTransaction</c> or to the policy
    /// emitter. § SH-H037 / TASK-215.
    /// </summary>
    [Fact]
    public void Refreshing_inside_a_transaction_is_refused_and_names_both_ways_out()
    {
        if (!RequireServer()) return;
        Reset();

        var runner = new TimescaleDBMigrationRunner(new TimescaleDBConnector(Settings()));   // UseTransaction = true
        runner.RegisterMigrations(new CreateThenRefreshMigration());
        runner.Initialize();

        var act = () => runner.Migrate();

        var refusal = act.Should().Throw<Exception>().Which;
        var message = refusal.InnerException?.Message ?? refusal.Message;

        message.Should().Contain("AddContinuousAggregatePolicy",
            "the idiomatic, transaction-safe door must be named first");
        message.Should().Contain("UseTransaction = false",
            "and the immediate-backfill door too — a refusal that only says 'no' gets reached around");
    }

    /// <summary>
    /// <b>The opt-out, and it is the half § SH-H037 insists on:</b> a guard whose escape hatch does not open
    /// is a wall wearing a door's label. Verified reachable in source too —
    /// <c>SqlMigrationRunner.ExecuteWithoutTransaction</c> passes a null transaction — but asserted here
    /// end to end, against a live server, because that is the claim.
    /// </summary>
    [Fact]
    public void With_the_opt_out_the_refresh_runs_and_populates_the_aggregate()
    {
        if (!RequireServer()) return;
        Reset();

        var runner = new TimescaleDBMigrationRunner(
            new TimescaleDBConnector(Settings()),
            new SqlMigrationSettings { UseTransaction = false });
        runner.RegisterMigrations(new CreateThenRefreshMigration());
        runner.Initialize();

        runner.Migrate().Success.Should().BeTrue("UseTransaction = false is the door the refusal names");

        Scalar($"SELECT COUNT(*) FROM \"{Aggregate}\"").Should().Be("2",
            "three rows spanning two days bucket into two daily buckets — the door does not merely open, "
          + "it leads somewhere");
    }

    /// <summary>Creates the aggregate and attaches a refresh policy — all inside the runner's transaction.</summary>
    private sealed class PolicyMigration : TimescaleDBMigration
    {
        public override long Version => 1;
        public override string Name => "create with a refresh policy";
        public override string Description => "TASK-281 policy path";
        public override DateTime CreatedAt => new(2026, 8, 24, 0, 0, 0, DateTimeKind.Utc);

        public override void Up(IMigrationContext context)
        {
            Raw(context, $"CREATE TABLE IF NOT EXISTS \"{Table}\" (Ts timestamptz NOT NULL, Value double precision)");
            CreateHypertable(context, Table, "Ts", "1 day");
            CreateContinuousAggregate(context, Aggregate, Table, "1 day", "Ts", new[] { ContinuousAggregateProjection.Of("avg", "Value", "avg_value") });
            AddContinuousAggregatePolicy(context, Aggregate, "30 days", "1 hour", "1 hour");
        }

        public override void Down(IMigrationContext context) { }

        private static void Raw(IMigrationContext context, string sql)
        {
            var c = (Birko.Data.Migrations.SQL.Context.SqlMigrationContext)context;
            using var command = c.Connection.CreateCommand();
            command.Transaction = c.Transaction;
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// <b>The workflow that makes option A honest.</b> Create (WITH NO DATA) plus a refresh policy is the
    /// complete, fully transactional way to stand up a continuous aggregate — measured legal, and the job
    /// survives the commit. Without this emitter the fix would have documented a remedy the framework could
    /// not perform, which is TASK-263's recorded failure.
    /// </summary>
    [Fact]
    public void A_refresh_policy_can_be_attached_inside_the_default_transactional_runner()
    {
        if (!RequireServer()) return;
        Reset();

        var runner = new TimescaleDBMigrationRunner(new TimescaleDBConnector(Settings()));   // UseTransaction = true
        runner.RegisterMigrations(new PolicyMigration());
        runner.Initialize();

        runner.Migrate().Success.Should().BeTrue(
            "add_continuous_aggregate_policy IS transaction-safe, unlike refresh_continuous_aggregate");

        Scalar("SELECT COUNT(*) FROM timescaledb_information.jobs "
             + $"WHERE proc_name = 'policy_refresh_continuous_aggregate' AND hypertable_name = '{Aggregate}'")
            .Should().Be("1", "assert the job in the catalogue, not merely that nothing threw (TASK-209)");
    }

    /// <summary>
    /// <b>The limit that makes the policy NOT a synonym for "populate", pinned rather than merely
    /// documented.</b> A refresh policy covers the moving window
    /// <c>[now() - startOffset, now() - endOffset]</c>, so history older than <c>startOffset</c> is never
    /// materialised — silently, since an under-filled aggregate reads exactly like a correct one.
    /// <para>
    /// Found by <c>code-review</c> at TASK-281's close gate, against documentation this task had just
    /// written calling the policy "the transaction-safe way to populate an aggregate" full stop. It is only
    /// that for a null <c>startOffset</c>. The doc comments now say so, and this is what keeps them honest —
    /// TASK-263's rule, that a named escape hatch has to actually open.
    /// </para>
    /// </summary>
    [Fact]
    public void A_refresh_policy_covers_a_moving_window_and_leaves_older_history_unmaterialised()
    {
        if (!RequireServer()) return;
        Reset();

        var runner = new TimescaleDBMigrationRunner(new TimescaleDBConnector(Settings()));
        runner.RegisterMigrations(new PolicyMigration());        // startOffset "30 days"
        runner.Initialize();
        runner.Migrate().Success.Should().BeTrue();

        // One row far outside the window, one inside it.
        Exec($"INSERT INTO \"{Table}\" (Ts, Value) VALUES "
           + "(now() - INTERVAL '400 days', 10), (now() - INTERVAL '2 days', 20)");

        var jobId = Scalar("SELECT job_id FROM timescaledb_information.jobs "
                         + $"WHERE proc_name = 'policy_refresh_continuous_aggregate' AND hypertable_name = '{Aggregate}'");
        Exec($"CALL run_job({jobId})");

        Scalar($"SELECT COUNT(*) FROM \"{Aggregate}\"").Should().Be("1",
            "the 400-day-old bucket falls outside [now() - 30 days, now() - 1 hour] and is never "
          + "materialised — so a policy keeps an aggregate CURRENT, it does not backfill history");

        Scalar($"SELECT avg_value FROM \"{Aggregate}\"").Should().Be("20",
            "and the one bucket that exists is the recent one, not the old one");
    }
}
