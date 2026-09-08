using System;
using Birko.Data.Migrations.TimescaleDB;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.TimescaleDB.Stores;
using FluentAssertions;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.Migrations.TimescaleDB.Tests;

/// <summary>
/// TASK-284 — a refresh policy's <c>start_offset</c>, against a live server.
///
/// <para>
/// Two findings, and the live server is what settles both. <c>NULL</c> means <b>"refresh from the
/// beginning of time"</b>, and <c>BuildContinuousAggregatePolicySql</c> used
/// <c>string.IsNullOrEmpty</c> — so a configuration value that came back as <c>""</c> rather than null
/// silently produced a far heavier policy than the author intended: every chunk, on every run of the job,
/// with no error anywhere. Every neighbouring interval in that class already failed loudly on an empty
/// string; only this one converted empty into semantically wider.
/// </para>
///
/// <para>
/// ⚠ <b>And the door the refusal names had never been opened.</b>
/// <c>RefreshContinuousAggregate</c>'s message tells the caller to pass a null <c>startOffset</c>
/// instead — a path asserted only as a <i>string</i> by the rendering tests. Since
/// <c>add_continuous_aggregate_policy</c> declares its parameters <c>"any"</c>, an untyped bare
/// <c>NULL</c> is exactly the kind of argument a server can reject, which would make the refusal
/// § SH-H037's <i>wall wearing a door's label</i>. Measured on 2.29.2 before anything was changed: it is
/// <b>accepted</b>. That is a valid outcome and this test is what keeps it true.
/// </para>
///
/// <para>Gated on <c>BIRKO_TS_HOST</c>; set <c>BIRKO_REQUIRE_LIVE</c> to make its absence a failure.</para>
/// </summary>
public class ContinuousAggregatePolicyOffsetLiveTests : IDisposable
{
    private const string Table = "OffMetrics";
    private const string Aggregate = "OffDailyStats";

    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_TS_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_TS_PORT"), out var p) ? p : 5432;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_TS_USER") ?? "postgres";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_TS_PASSWORD") ?? "postgres";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_TS_DB") ?? "birkoview";
    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _output;

    public ContinuousAggregatePolicyOffsetLiveTests(ITestOutputHelper output) => _output = output;

    private bool RequireServer()
    {
        if (!string.IsNullOrWhiteSpace(Host)) return true;
        const string message = "SKIPPED: no live TimescaleDB. Set BIRKO_TS_HOST to exercise this test; "
                             + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (RequireLive) throw new InvalidOperationException(message);
        return false;
    }

    private static TimescaleDBSettings Settings()
        => new(Host!, Database, User, Password, Port, "ts", "1 day");

    private static TimescaleDBConnector Connector() => new(Settings());

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

    /// <summary>Builds the hypertable and an empty aggregate for a policy to attach to.</summary>
    private static void Seed()
    {
        Exec($"DROP MATERIALIZED VIEW IF EXISTS \"{Aggregate}\" CASCADE");
        Exec($"DROP TABLE IF EXISTS \"{Table}\" CASCADE");
        Exec($"CREATE TABLE \"{Table}\" (ts timestamptz NOT NULL, v double precision)");
        Exec($"SELECT create_hypertable('\"{Table}\"', 'ts')");
        Exec($"CREATE MATERIALIZED VIEW \"{Aggregate}\" WITH (timescaledb.continuous) AS "
           + $"SELECT time_bucket('1 day', ts) AS bucket, avg(v) AS avg_v FROM \"{Table}\" "
           + $"GROUP BY bucket WITH NO DATA");
    }

    /// <summary>
    /// The refresh policy attached to <b>this</b> aggregate: its <c>start_offset</c>, or
    /// <c>&lt;null&gt;</c> when a policy exists with a null offset, or <c>&lt;none&gt;</c> when there is
    /// no policy at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠ <b>Scoped to this view, not to "the only policy".</b> The first version queried every
    /// <c>policy_refresh_continuous_aggregate</c> job in the database and took the first, which read a
    /// sibling live class's policy — and a leftover from a manual probe — as this test's own.
    /// </para>
    /// <para>
    /// ⚠ <b>And the obvious join was wrong.</b> A job looks like it should be joined to
    /// <c>continuous_aggregates.materialization_hypertable_name</c>, since its config carries a
    /// <c>mat_hypertable_id</c>. Measured on 2.29.2: <c>jobs.hypertable_name</c> is the <b>view</b> name
    /// (<c>OffDailyStats</c>), while the materialisation hypertable is
    /// <c>_materialized_hypertable_16</c> — so the join matched nothing and every assertion read
    /// <c>&lt;none&gt;</c>. Filtering on the view name directly is both correct and simpler.
    /// </para>
    /// <para>
    /// The three-way answer matters: <c>&lt;none&gt;</c> and <c>&lt;null&gt;</c> must be distinguishable,
    /// or "no policy was created" and "a policy over all of history was created" would assert the same.
    /// </para>
    /// </remarks>
    private static string StartOffsetOfThisAggregatesPolicy()
        => Scalar($"SELECT coalesce(max(config->>'start_offset'), "
                + $"       CASE WHEN count(*) = 0 THEN '<none>' ELSE '<null>' END) "
                + $"FROM timescaledb_information.jobs "
                + $"WHERE proc_name = 'policy_refresh_continuous_aggregate' "
                + $"  AND hypertable_name = '{Aggregate}'");

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(Host)) return;
        try { Exec($"DROP MATERIALIZED VIEW IF EXISTS \"{Aggregate}\" CASCADE"); } catch { }
        try { Exec($"DROP TABLE IF EXISTS \"{Table}\" CASCADE"); } catch { }
    }

    /// <summary>
    /// The escape hatch, proven to open. This is the path <c>RefreshContinuousAggregate</c>'s refusal tells
    /// a caller to take, and until now it existed only as an expected string.
    /// </summary>
    [Fact]
    public void A_null_start_offset_really_is_accepted_by_the_server()
    {
        if (!RequireServer()) return;
        Seed();

        Exec(TimescaleDBMigration.BuildContinuousAggregatePolicySql(
            Connector(), Aggregate, null, "1 hour", "1 hour"));

        var recorded = StartOffsetOfThisAggregatesPolicy();
        _output.WriteLine($"start_offset recorded by the server: {recorded}");

        recorded.Should().Be("<null>",
            "a bare untyped NULL must be accepted despite add_continuous_aggregate_policy declaring its "
            + "parameters \"any\", or the refusal that points callers here is a wall wearing a door's label");
    }

    /// <summary>
    /// ⚠ The defect: an empty offset must NOT reach the server as <c>NULL</c>. Before TASK-284 this
    /// produced a policy over all of history, silently.
    /// </summary>
    [Fact]
    public void An_empty_start_offset_is_refused_rather_than_widening_the_policy()
    {
        if (!RequireServer()) return;
        Seed();

        var sql = TimescaleDBMigration.BuildContinuousAggregatePolicySql(
            Connector(), Aggregate, "", "1 hour", "1 hour");

        Action attach = () => Exec(sql);

        attach.Should().Throw<PostgresException>(
                "an empty interval must fail loudly, exactly as every other interval in this class does")
            .Which.SqlState.Should().Be("22007", "invalid input syntax for type interval");

        StartOffsetOfThisAggregatesPolicy().Should().Be("<none>",
            "and no policy may have been created at all — <none> is no row, which the helper reports "
            + "distinctly from <null> precisely so this assertion cannot be satisfied by a policy that "
            + "WAS created with a null offset");
    }

    /// <summary>
    /// The control that keeps the two tests above honest: a real offset produces a bounded policy, so
    /// <c>&lt;null&gt;</c> in the first test means "the server recorded null" rather than "the query found
    /// nothing".
    /// </summary>
    [Fact]
    public void A_real_start_offset_produces_a_bounded_policy()
    {
        if (!RequireServer()) return;
        Seed();

        Exec(TimescaleDBMigration.BuildContinuousAggregatePolicySql(
            Connector(), Aggregate, "30 days", "1 hour", "1 hour"));

        StartOffsetOfThisAggregatesPolicy().Should().Be("30 days");
    }
}
