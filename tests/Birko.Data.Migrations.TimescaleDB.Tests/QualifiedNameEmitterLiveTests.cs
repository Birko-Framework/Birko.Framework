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
/// TASK-262 — a <b>schema-qualified</b> object name reaches the real object.
///
/// <para>
/// <b>The regression this pins.</b> TASK-253 routed every regclass and object-name argument through
/// <c>QuoteIdentifier</c>, which quotes its whole argument as ONE identifier. That is right for a name from
/// <c>Table.Name</c> (never qualified) and wrong for a name a migration author wrote: <c>reporting.evts</c>
/// became <c>'"reporting.evts"'</c>, i.e. a request for a single table whose name contains a period. Measured
/// on TimescaleDB 2.29.2 / PostgreSQL 16.15 — <c>42P01</c>, which
/// <c>PostgreSQLConnector.IsMissingTableException</c> classifies as a missing table, so the handler could
/// swallow it and report success. Schema qualification is idiomatic in a migration and appears nowhere else in
/// the surface TASK-253 changed, which is how it was missed.
/// </para>
/// <para>
/// The fix is <c>AbstractConnectorBase.QualifiedIdentifier</c>: split on <b>unquoted</b> dots and quote each
/// part. Strictly more capable than the bare form that preceded TASK-253 — a bare qualified name resolves, but
/// a bare mixed-case or spaced part does not, and per-part quoting handles all three.
/// </para>
/// <para>
/// Every assertion reads the <c>timescaledb_information</c> catalogue, never "the call did not throw"
/// (TASK-209), and reads it with case intact.
/// </para>
/// </summary>
public class QualifiedNameEmitterLiveTests : IDisposable
{
    private const string Schema = "reporting";
    private const string Table = "QualMetrics";
    private const string MixedSchema = "Rep Ort";
    private const string MixedTable = "Ev ts";

    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_TS_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_TS_PORT"), out var p) ? p : 5432;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_TS_USER") ?? "postgres";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_TS_PASSWORD") ?? "postgres";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_TS_DB") ?? "birkoview";
    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _output;

    public QualifiedNameEmitterLiveTests(ITestOutputHelper output) => _output = output;

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
    /// Matches on schema AND name, with case intact. Matching on the name alone would let a hypertable created
    /// in the wrong schema pass — which is precisely the failure mode under test.
    /// </summary>
    private static bool IsHypertableIn(string schema, string table) => Scalar(
        "SELECT COUNT(*) FROM timescaledb_information.hypertables "
      + $"WHERE hypertable_schema = '{schema.Replace("'", "''")}' "
      + $"AND hypertable_name = '{table.Replace("'", "''")}'") == "1";

    private static (NpgsqlConnection connection, IMigrationContext context) NewContext()
    {
        var connection = new NpgsqlConnection(Settings().GetConnectionString());
        connection.Open();
        return (connection, new TimescaleDBMigrationContext(connection, null, new TimescaleDBConnector(Settings())));
    }

    private sealed class Probe : TimescaleDBMigration
    {
        public override long Version => 1;
        public override string Name => nameof(Probe);
        public override string Description => "TASK-262 qualified-name probe";
        public override DateTime CreatedAt => new(2026, 8, 21, 0, 0, 0, DateTimeKind.Utc);
        public override void Up(IMigrationContext context) { }
        public override void Down(IMigrationContext context) { }

        public void Hypertable(IMigrationContext c, string t, string col) => CreateHypertable(c, t, col);
        public void HypertableWithInterval(IMigrationContext c, string t, string col, string interval)
            => CreateHypertable(c, t, col, interval);
        public void Retention(IMigrationContext c, string t, string after) => AddRetentionPolicy(c, t, after);
        public bool Hyper(IMigrationContext c, string t) => IsHypertable(c, t);
        public string? Chunk(IMigrationContext c, string t) => GetChunkInterval(c, t);
    }

    private void Reset()
    {
        Exec($"DROP SCHEMA IF EXISTS \"{Schema}\" CASCADE");
        Exec($"DROP SCHEMA IF EXISTS \"{MixedSchema}\" CASCADE");
    }

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(Host)) return;
        try { Reset(); } catch { }
    }

    [Fact]
    public void A_schema_qualified_table_becomes_a_hypertable_in_that_schema()
    {
        if (!RequireServer()) return;
        Reset();
        Exec($"CREATE SCHEMA \"{Schema}\"");
        Exec($"CREATE TABLE \"{Schema}\".\"{Table}\" (ts timestamptz NOT NULL, v double precision)");

        var (conn, ctx) = NewContext();
        using (conn)
        {
            new Probe().Hypertable(ctx, $"{Schema}.{Table}", "ts");
        }

        IsHypertableIn(Schema, Table).Should().BeTrue(
            "before TASK-262 this emitted '\"reporting.QualMetrics\"' -- one identifier containing a dot -- "
          + "which raises 42P01 and is classified as a missing table, so the failure could be swallowed and "
          + "no hypertable would exist while the call reported success");
    }

    /// <summary>
    /// Per-part quoting also reaches names a bare qualified reference never could, which is why the fix is
    /// better than reverting TASK-253 rather than merely equivalent to it.
    /// </summary>
    [Fact]
    public void A_qualified_name_whose_parts_need_quoting_also_works()
    {
        if (!RequireServer()) return;
        Reset();
        Exec($"CREATE SCHEMA \"{MixedSchema}\"");
        Exec($"CREATE TABLE \"{MixedSchema}\".\"{MixedTable}\" (ts timestamptz NOT NULL, v int)");

        var (conn, ctx) = NewContext();
        using (conn)
        {
            new Probe().Hypertable(ctx, $"\"{MixedSchema}\".\"{MixedTable}\"", "ts");
        }

        IsHypertableIn(MixedSchema, MixedTable).Should().BeTrue(
            "spaces and mixed case in both parts -- unreachable through the bare form that preceded TASK-253");
    }

    /// <summary>
    /// A policy emitter takes the same regclass, so fixing only <c>create_hypertable</c> would leave five
    /// other emitters broken for the same reason. One producer, so one fix — asserted rather than assumed.
    /// </summary>
    [Fact]
    public void A_policy_emitter_accepts_the_same_qualified_name()
    {
        if (!RequireServer()) return;
        Reset();
        Exec($"CREATE SCHEMA \"{Schema}\"");
        Exec($"CREATE TABLE \"{Schema}\".\"{Table}\" (ts timestamptz NOT NULL, v double precision)");

        var (conn, ctx) = NewContext();
        using (conn)
        {
            var probe = new Probe();
            probe.Hypertable(ctx, $"{Schema}.{Table}", "ts");
            probe.Retention(ctx, $"{Schema}.{Table}", "30 days");
        }

        var policies = int.Parse(Scalar(
            "SELECT COUNT(*) FROM timescaledb_information.jobs "
          + $"WHERE proc_name = 'policy_retention' AND hypertable_schema = '{Schema}' "
          + $"AND hypertable_name = '{Table}'"));
        policies.Should().Be(1, "add_retention_policy takes a regclass through the same producer");
    }

    /// <summary>
    /// ⚠ <b>This test used to assert the defect as a "documented limitation".</b> It created the hypertable
    /// as <c>reporting.QualMetrics</c> and then asked <c>IsHypertable</c> for the <b>bare</b> name, because
    /// the qualified form answered <see langword="false"/> — a hypertable that exists reported as absent.
    /// TASK-280 fixed it, so the probe is now asked for the name it was actually given.
    /// </summary>
    [Fact]
    public void The_hypertable_probe_answers_for_a_qualified_table()
    {
        if (!RequireServer()) return;
        Reset();
        Exec($"CREATE SCHEMA \"{Schema}\"");
        Exec($"CREATE TABLE \"{Schema}\".\"{Table}\" (ts timestamptz NOT NULL, v double precision)");

        var (conn, ctx) = NewContext();
        using (conn)
        {
            var probe = new Probe();
            probe.Hypertable(ctx, $"{Schema}.{Table}", "ts");

            probe.Hyper(ctx, $"{Schema}.{Table}").Should().BeTrue(
                "the probe must answer for the same name the emitter accepted -- both resolve it through "
              + "the server's own regclass resolver, so a name means one thing on both doors");
        }
    }

    /// <summary>
    /// The fixture that distinguishes a fix from a no-op (TASK-280): the <b>same table name in two
    /// schemas</b>, each with a different chunk interval. A single-schema database cannot tell a
    /// schema-aware lookup from a name-only one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Measured on 2.29.2 before the fix: the name-only <c>WHERE hypertable_name = @table</c> matched
    /// <b>2</b> rows for an unqualified name and <c>ExecuteScalar</c> took whichever the planner emitted
    /// first — so <c>GetChunkInterval</c> could return <c>1 day</c> or <c>7 days</c> arbitrarily. Same
    /// shape as TASK-261's <c>dimension_number</c> finding one level up: a view with one row per object
    /// needs its row pinned, and <c>ExecuteScalar</c> will not tell you.
    /// </para>
    /// <para>
    /// The unqualified case asserts the <c>search_path</c> answer (<c>public</c>), which is not a
    /// preference: it is what <c>create_hypertable</c>'s own <c>::regclass</c> does, so the reader and the
    /// emitter agree about what an unqualified name means.
    /// </para>
    /// </remarks>
    [Fact]
    public void Two_schemas_holding_the_same_table_name_get_their_own_answers()
    {
        if (!RequireServer()) return;
        Reset();
        Exec($"DROP TABLE IF EXISTS \"{Table}\" CASCADE");
        Exec($"CREATE SCHEMA \"{Schema}\"");
        Exec($"CREATE TABLE \"{Table}\" (ts timestamptz NOT NULL, v double precision)");
        Exec($"CREATE TABLE \"{Schema}\".\"{Table}\" (ts timestamptz NOT NULL, v double precision)");

        var (conn, ctx) = NewContext();
        using (conn)
        {
            var probe = new Probe();
            // Deliberately different intervals, so the wrong schema's answer is distinguishable.
            probe.HypertableWithInterval(ctx, Table, "ts", "1 day");
            probe.HypertableWithInterval(ctx, $"{Schema}.{Table}", "ts", "7 days");

            probe.Hyper(ctx, Table).Should().BeTrue();
            probe.Hyper(ctx, $"{Schema}.{Table}").Should().BeTrue();

            probe.Chunk(ctx, $"{Schema}.{Table}").Should().Be("7 days",
                "the qualified name must read ITS schema's dimension, not whichever row came first");
            probe.Chunk(ctx, Table).Should().Be("1 day",
                "and an unqualified name follows the search_path, which is what create_hypertable's own "
              + "regclass did when it created the thing");
        }

        Exec($"DROP TABLE IF EXISTS \"{Table}\" CASCADE");
    }
}
