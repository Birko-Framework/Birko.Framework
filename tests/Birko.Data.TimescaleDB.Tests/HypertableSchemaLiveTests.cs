using System;
using System.Linq;
using Birko.Data.Models;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.TimescaleDB.Stores;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.TimescaleDB.Tests;

/// <summary>
/// <b>The premise every other TimescaleDB test rests on: schema-ensure actually produces a hypertable.</b>
///
/// <para>
/// TASK-472 asked for the bulk transaction boundary to be verified "over a real hypertable", because
/// <c>TimescaleDBConnector : PostgreSQLConnector</c> overrides no bulk method and therefore <i>inherits</i>
/// the fix rather than receiving one. Establishing the premise is what found the larger defect:
/// <b>no hypertable had ever been created for a PascalCase-named entity</b> — which is every Birko entity
/// by convention — so a "verification over a hypertable" would have run against a plain PostgreSQL table
/// and proved nothing at all.
/// </para>
///
/// <para>
/// <c>create_hypertable</c> takes its table as a <c>regclass</c> and its time column as a <c>name</c>, both
/// arriving inside quoted string literals. The parser's identifier folding never runs on a literal, so the
/// table needed its own quotes (bare, <c>'TsSchemaRows'</c> resolved to <c>tsschemarows</c> → <c>42P01</c>,
/// which <c>IsMissingTableException</c> classifies as a missing table and the handler swallows) while the
/// column needed the opposite — pre-folding, since the framework emits column definitions bare and
/// PostgreSQL stored <c>ts</c> against a request for <c>Ts</c> (<c>42703</c>, not swallowed). Measured on
/// TimescaleDB 2 / PostgreSQL 16.
/// </para>
///
/// <para>
/// <b>Each model type is mapped exactly once, in a static constructor.</b> <c>ModelMapRegistry</c> applies
/// into process-wide <c>DataBase</c> state and <i>accumulates</i>, so registering two different mappings for
/// one type merges them: the first draft of this suite mapped one type two ways and got
/// <c>42P16 multiple primary keys</c> from the union of both, which looks exactly like a product defect.
/// </para>
///
/// <para>
/// Gated on <c>BIRKO_TS_HOST</c> (+ <c>_PORT</c> / <c>_USER</c> / <c>_PASSWORD</c> / <c>_DB</c>), and a
/// skipped run says so out loud — see <see cref="RequireServer"/>.
/// </para>
/// </summary>
public class HypertableSchemaLiveTests : IDisposable
{
    private const string TimeKeyedTable = "TsSchemaRows";
    private const string GuidKeyedTable = "TsGuidKeyedRows";

    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_TS_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_TS_PORT"), out var p) ? p : 5432;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_TS_USER") ?? "postgres";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_TS_PASSWORD") ?? "postgres";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_TS_DB") ?? "birkoview";
    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _output;

    public HypertableSchemaLiveTests(ITestOutputHelper output) => _output = output;

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

    /// <summary>
    /// <c>TimeColumn</c> defaults to the PascalCase <c>"Ts"</c>, matching the property. The framework's own
    /// default is the already-lowercase <c>"timestamp"</c>, which matches a folded <c>Timestamp</c> column
    /// by luck — so a suite using that default cannot see the column half of the defect at all.
    /// </summary>
    private static TimescaleDBSettings Settings(string timeColumn = "Ts", string chunk = "1 day")
        => new(Host!, Database, User, Password, Port, timeColumn, chunk);

    /// <summary>
    /// The time column carries the primary key, and that is the only shape a Birko hypertable can have:
    /// TimescaleDB refuses a unique index that omits the partitioning column, and this framework emits
    /// <c>PRIMARY KEY</c> <i>per column</i>, so a composite <c>(Guid, Ts)</c> key is not expressible — two
    /// <c>HasPrimary</c> calls emit two <c>PRIMARY KEY</c> clauses and PostgreSQL rejects the DDL with
    /// <c>42P16</c>. Both halves are pinned below.
    /// </summary>
    public class TimeKeyedRow : AbstractModel
    {
        public string? Name { get; set; }
        public DateTime Ts { get; set; }
    }

    /// <summary>A Guid-keyed entity: ordinary PostgreSQL, and illegal on a hypertable.</summary>
    public class GuidKeyedRow : AbstractModel
    {
        public string? Name { get; set; }
        public DateTime Ts { get; set; }
    }

    private sealed class TimeKeyedMapping : IModelMapping<TimeKeyedRow>
    {
        public void Configure(ModelMap<TimeKeyedRow> map)
        {
            map.ToTable(TimeKeyedTable).HasPrimary(x => x.Ts);
            map.Property(x => x.Name).HasPrecision(100);
            map.Property(x => x.Ts);
        }
    }

    private sealed class GuidKeyedMapping : IModelMapping<GuidKeyedRow>
    {
        public void Configure(ModelMap<GuidKeyedRow> map)
        {
            map.ToTable(GuidKeyedTable).HasPrimary(x => x.Guid).HasUnique(x => x.Guid);
            map.Property(x => x.Name).HasPrecision(100);
            map.Property(x => x.Ts);
        }
    }

    static HypertableSchemaLiveTests()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new TimeKeyedMapping());
        registry.Register(new GuidKeyedMapping());
        registry.ApplyToDatabase();
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

    /// <summary>
    /// Asks the TimescaleDB catalogue, never "did the call throw" — <c>CreateView</c> taught this framework
    /// (TASK-209) that a swallowing DDL layer makes a no-op indistinguishable from success. The name is
    /// compared with its <b>case intact</b>: <c>CreateTable</c> quotes the table, so the catalogue holds
    /// <c>TsSchemaRows</c>. Lower-casing it here produced a false negative that briefly looked like the fix
    /// having failed.
    /// </summary>
    private static bool IsHypertable(string table) => Scalar(
        $"SELECT COUNT(*) FROM timescaledb_information.hypertables WHERE hypertable_name = '{table}'") == "1";

    private static void DropBoth()
    {
        Exec($"DROP TABLE IF EXISTS \"{TimeKeyedTable}\" CASCADE");
        Exec($"DROP TABLE IF EXISTS \"{GuidKeyedTable}\" CASCADE");
    }

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(Host)) return;
        try { DropBoth(); } catch { }
    }

    // ================================================================ the premise

    [Fact]
    public void Schema_ensure_converts_a_PascalCase_table_into_a_real_hypertable()
    {
        if (!RequireServer()) return;
        DropBoth();

        new TimescaleDBConnector(Settings()).CreateTable(new[] { typeof(TimeKeyedRow) });

        Scalar($"SELECT to_regclass('\"{TimeKeyedTable}\"') IS NOT NULL").Should().Be("True");
        IsHypertable(TimeKeyedTable).Should().BeTrue(
            "create_hypertable must reach the table it was given. Emitted bare, the regclass folded to "
          + "'tsschemarows', raised 42P01, and IsMissingTableException swallowed it — so CreateTable "
          + "reported success and left a plain PostgreSQL table behind, on every PascalCase entity there "
          + "has ever been");
    }

    /// <summary>
    /// The column half, isolated. Quoting the table alone is not enough — it merely moves the failure from
    /// the (swallowed) relation lookup to the (loud) column lookup.
    /// </summary>
    [Fact]
    public void The_time_column_is_matched_against_the_folded_stored_name()
    {
        if (!RequireServer()) return;
        DropBoth();

        new TimescaleDBConnector(Settings(timeColumn: "Ts")).CreateTable(new[] { typeof(TimeKeyedRow) });

        // TimeColumn is "Ts"; the stored column is "ts", because column definitions are emitted bare.
        Scalar($"SELECT string_agg(column_name, ',' ORDER BY column_name) FROM information_schema.columns "
             + $"WHERE table_name = '{TimeKeyedTable}'")
            .Should().Contain("ts").And.NotContain("Ts");

        IsHypertable(TimeKeyedTable).Should().BeTrue(
            "the name argument is compared literally against pg_attribute.attname, so it has to be "
          + "pre-folded — the parser never folds it, because it never sees an identifier");
    }

    /// <summary>
    /// An already-lowercase <c>TimeColumn</c> must keep working — it is the shipped default and the only
    /// configuration that ever worked, so the fold must not have broken it.
    /// </summary>
    [Fact]
    public void An_already_lowercase_time_column_still_works()
    {
        if (!RequireServer()) return;
        DropBoth();

        new TimescaleDBConnector(Settings(timeColumn: "ts")).CreateTable(new[] { typeof(TimeKeyedRow) });

        IsHypertable(TimeKeyedTable).Should().BeTrue();
    }

    [Fact]
    public void Rows_days_apart_land_in_separate_chunks()
    {
        if (!RequireServer()) return;
        DropBoth();
        new TimescaleDBConnector(Settings(chunk: "1 day")).CreateTable(new[] { typeof(TimeKeyedRow) });

        Exec($"INSERT INTO \"{TimeKeyedTable}\" (Guid, Name, Ts) VALUES "
           + "('11111111-1111-1111-1111-111111111111', 'a', '2026-01-01 00:00:00'), "
           + "('22222222-2222-2222-2222-222222222222', 'b', '2026-02-01 00:00:00'), "
           + "('33333333-3333-3333-3333-333333333333', 'c', '2026-03-01 00:00:00')");

        int.Parse(Scalar($"SELECT COUNT(*) FROM timescaledb_information.chunks "
                       + $"WHERE hypertable_name = '{TimeKeyedTable}'"))
            .Should().BeGreaterThan(1,
                "chunk routing is the thing a hypertable does that a plain table does not — if this is 0 "
              + "the table is not partitioned and every hypertable-specific claim above is vacuous");
    }

    // ================================================================ what a hypertable refuses

    /// <summary>
    /// <b>Recorded, not endorsed.</b> A Guid-keyed entity cannot be a hypertable, and after the identifier
    /// fix that arrives as a thrown <c>TS103</c> out of schema-ensure instead of the silent plain table it
    /// used to produce. The throw is correct — the mapping genuinely cannot be honoured — but it leaves the
    /// store permanently uninitialised, which is the failure mode TASK-204 removed for indexes
    /// ("lazy schema-ensure degrades and reports"). Pinned here so the behaviour is deliberate rather than
    /// accidental; making it degrade-and-report is filed separately.
    /// </summary>
    [Fact]
    public void A_guid_keyed_entity_cannot_be_a_hypertable_and_now_says_so()
    {
        if (!RequireServer()) return;
        DropBoth();

        var connector = new TimescaleDBConnector(Settings());
        var act = () => connector.CreateTable(new[] { typeof(GuidKeyedRow) });

        act.Should().Throw<Exception>()
            .Where(ex => Flatten(ex).Contains("TS103") || Flatten(ex).Contains("unique index"),
                "TimescaleDB refuses a unique index that omits the partitioning column; before the "
              + "identifier fix this never got far enough to be raised, because the statement failed on "
              + "the relation name first and that failure was swallowed");

        IsHypertable(GuidKeyedTable).Should().BeFalse();
    }

    private static string Flatten(Exception ex)
    {
        var text = string.Empty;
        for (Exception? e = ex; e != null; e = e.InnerException)
        {
            text += e.Message + " | ";
        }
        return text;
    }
}
