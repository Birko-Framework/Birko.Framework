using System;
using System.Linq;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.TimescaleDB.Stores;
using FluentAssertions;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.Migrations.TimescaleDB.Tests;

/// <summary>
/// TASK-303 — the case a composite primary key exists for: a Guid-keyed hypertable.
///
/// <para>
/// TimescaleDB refuses a unique index that omits the partitioning column, so a Birko entity keyed on
/// <c>Guid</c> alone could not become a hypertable. Measured on 2.29.2, and the server states the remedy
/// itself:
/// </para>
/// <code>
/// ERROR:  cannot create a unique index without the column "ts" (used in partitioning)
/// HINT:   If you're creating a hypertable on a table with a primary key, ensure the partitioning
///         column is part of the primary or composite key.
/// </code>
/// <para>
/// Until TASK-303 the framework could not express that composite key — <c>FieldDefinition</c> rendered
/// <c>PRIMARY KEY</c> inline per column, so two primary fields emitted two clauses and the
/// <c>CREATE TABLE</c> itself failed. So <c>(Guid, Ts)</c>, the natural telemetry shape and the first
/// thing a consumer reaches for, was inexpressible. This is the root cause underneath [[TASK-254]].
/// </para>
///
/// <para>Gated on <c>BIRKO_TS_HOST</c>; set <c>BIRKO_REQUIRE_LIVE</c> to make its absence a failure.</para>
/// </summary>
public class CompositeKeyHypertableLiveTests : IDisposable
{
    private const string CompositeTable = "CompKeyMetrics";
    private const string GuidOnlyTable = "GuidKeyMetrics";

    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_TS_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_TS_PORT"), out var p) ? p : 5432;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_TS_USER") ?? "postgres";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_TS_PASSWORD") ?? "postgres";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_TS_DB") ?? "birkoview";
    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _output;

    public CompositeKeyHypertableLiveTests(ITestOutputHelper output) => _output = output;

    private bool RequireServer()
    {
        if (!string.IsNullOrWhiteSpace(Host)) return true;
        const string message = "SKIPPED: no live TimescaleDB. Set BIRKO_TS_HOST to exercise this test; "
                             + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (RequireLive) throw new InvalidOperationException(message);
        return false;
    }

    /// <summary>The partitioning column is <c>ts</c>, matching the connector's configured TimeColumn.</summary>
    private static TimescaleDBSettings Settings()
        => new(Host!, Database, User, Password, Port, "ts", "1 day");

    /// <summary>The shape a consumer reaches for: identity plus time, both in the key.</summary>
    [Table(CompositeTable)]
    public class CompositeKeyRow
    {
        [PrimaryField]
        public Guid? Guid { get; set; }

        [PrimaryField]
        [RequiredField]
        public DateTime Ts { get; set; }

        public double Value { get; set; }
    }

    /// <summary>The control: identity only, which TimescaleDB refuses to convert.</summary>
    [Table(GuidOnlyTable)]
    public class GuidOnlyRow
    {
        [PrimaryField]
        public Guid? Guid { get; set; }

        [RequiredField]
        public DateTime Ts { get; set; }

        public double Value { get; set; }
    }

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

    private static void Drop()
    {
        Exec($"DROP TABLE IF EXISTS \"{CompositeTable}\" CASCADE");
        Exec($"DROP TABLE IF EXISTS \"{GuidOnlyTable}\" CASCADE");
    }

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(Host)) return;
        try { Drop(); } catch { }
    }

    /// <summary>
    /// The whole point: a <c>(Guid, Ts)</c> entity becomes a hypertable, where a Guid-only one cannot.
    /// </summary>
    [Fact]
    public void A_composite_keyed_entity_becomes_a_hypertable()
    {
        if (!RequireServer()) return;
        Drop();

        var connector = new TimescaleDBConnector(Settings());
        connector.CreateTable(new[] { typeof(CompositeKeyRow) });

        Scalar($"SELECT count(*) FROM timescaledb_information.hypertables "
             + $"WHERE hypertable_name = '{CompositeTable}'")
            .Should().Be("1", "the partitioning column is part of the key, which is what TimescaleDB asks for");

        // And the key really is composite, not just the last column standing.
        var key = Scalar(
            "SELECT string_agg(a.attname, ',' ORDER BY k.ord) "
          + "FROM pg_constraint c "
          + "JOIN LATERAL unnest(c.conkey) WITH ORDINALITY AS k(attnum, ord) ON true "
          + "JOIN pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = k.attnum "
          + $"WHERE c.conrelid = '\"{CompositeTable}\"'::regclass AND c.contype = 'p'");

        _output.WriteLine($"primary key columns: {key}");
        key.Should().Be("guid,ts", "declaration order, and both columns present");
    }

    /// <summary>
    /// ⚠ The control, and it is what makes the test above mean something: the same entity keyed on
    /// <c>Guid</c> alone is still refused by TimescaleDB. If this ever passes, the fix above is being
    /// credited for something the server stopped enforcing.
    /// </summary>
    [Fact]
    public void A_guid_only_entity_is_still_refused_by_TimescaleDB()
    {
        if (!RequireServer()) return;
        Drop();

        var connector = new TimescaleDBConnector(Settings());

        // TASK-254: the conversion failure is recorded rather than thrown, and the plain table survives.
        connector.CreateTable(new[] { typeof(GuidOnlyRow) });

        Scalar($"SELECT count(*) FROM timescaledb_information.hypertables "
             + $"WHERE hypertable_name = '{GuidOnlyTable}'")
            .Should().Be("0", "a unique index omitting the partitioning column is refused (TS103)");

        Scalar($"SELECT count(*) FROM information_schema.tables WHERE table_name = '{GuidOnlyTable}'")
            .Should().Be("1", "and TASK-254's degrade leaves a usable plain table behind");
    }
}
