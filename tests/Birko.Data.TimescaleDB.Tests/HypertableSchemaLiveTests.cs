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
    /// <b>TASK-254: schema-ensure DEGRADES when the conversion cannot be honoured.</b> This test previously
    /// pinned the opposite — a thrown <c>TS103</c> out of <c>CreateTable</c> — and said in its own summary
    /// that the throw left the store permanently uninitialised, which is the failure mode TASK-204 removed
    /// for indexes. It was recorded as deliberate rather than endorsed; this is the inversion it
    /// anticipated.
    /// <para>
    /// <b>Why degrading is legitimate here, and it is not "partitioning is only an optimisation".</b>
    /// Nothing ever declares an entity to be a hypertable: <c>TimescaleDBConnector.CreateTable</c> converts
    /// <i>every</i> table it creates whenever <c>TimescaleDBSettings.TimeColumn</c> is set, and there is no
    /// per-entity attribute anywhere. So a failed conversion is a connector-wide default that did not apply,
    /// not a broken per-entity contract — squarely TASK-204's "degrade only what is a constraint or an
    /// optimisation, never correctness".
    /// </para>
    /// <para>
    /// <b>The premise this rests on was measured before the fix was written</b> (TimescaleDB 2.29.2 /
    /// PostgreSQL 16.15): the plain table <i>survives</i> the failed <c>create_hypertable</c> and is fully
    /// writable and readable. Had it not, degrading would leave the store initialised over a table that does
    /// not exist — strictly worse than the throw it replaces. That is why this asserts a real write and read
    /// rather than merely that no exception escaped.
    /// </para>
    /// </summary>
    [Fact]
    public void A_guid_keyed_entity_degrades_to_a_plain_table_and_is_reported()
    {
        if (!RequireServer()) return;
        DropBoth();

        var connector = new TimescaleDBConnector(Settings());

        var act = () => connector.CreateTable(new[] { typeof(GuidKeyedRow) });
        act.Should().NotThrow(
            "an unconvertible hypertable must not take the entity's whole surface — reads included — with "
          + "it, which is what TASK-204 established for indexes and TASK-254 extends to this conversion");

        Scalar($"SELECT to_regclass('\"{GuidKeyedTable}\"') IS NOT NULL").Should().Be("True",
            "the table itself must exist: degrading is only defensible because the plain table survives");
        IsHypertable(GuidKeyedTable).Should().BeFalse("the conversion genuinely could not be honoured");

        // Usable, not merely present — the whole point of degrading.
        Exec($"INSERT INTO \"{GuidKeyedTable}\" (Guid, Name, Ts) VALUES "
           + "('44444444-4444-4444-4444-444444444444', 'd', '2026-01-01 00:00:00')");
        Scalar($"SELECT COUNT(*) FROM \"{GuidKeyedTable}\"").Should().Be("1");

        connector.HypertableCreationFailures.Should().ContainSingle(
            "the failure is reported, not swallowed — TASK-204's other half, and the reason this is not a "
          + "regression to the silent plain table that preceded TASK-472")
            .Which.TableName.Should().Be(GuidKeyedTable);
    }

    /// <summary>
    /// <b>The other half of TASK-204's split, and the reason it is not superficial pattern-matching.</b> A
    /// caller asking for the conversion <i>now</i> gets the error — exactly why <c>CreateIndexes</c> still
    /// throws there while schema-ensure degrades. This door is public and therefore has real callers.
    /// </summary>
    [Fact]
    public void An_explicit_conversion_still_throws()
    {
        if (!RequireServer()) return;
        DropBoth();

        var connector = new TimescaleDBConnector(Settings());
        connector.CreateTable(new[] { typeof(GuidKeyedRow) });   // degrades, leaving a plain table

        var act = () => connector.CreateHypertable(GuidKeyedTable, "ts");

        act.Should().Throw<Exception>()
            .Where(ex => Flatten(ex).Contains("TS103") || Flatten(ex).Contains("unique index"),
                "lazy schema-ensure degrades and reports; an EXPLICIT schema call throws (TASK-204)");
    }

    /// <summary>
    /// <b>The async explicit door, asserted rather than assumed.</b> TASK-254's criterion names
    /// <c>CreateHypertable</c> <i>and</i> <c>CreateHypertableAsync</c>, and the first version of this suite
    /// covered only the sync one — caught by <c>verify-intent</c> at the close gate. The async path is
    /// untouched by this task, so it throws by construction; "by construction" is precisely what this repo
    /// has been burned by four times (TASK-245: the async twin you patched may not be the one anything
    /// calls), and what criterion 3 forbids for the record.
    /// </summary>
    [Fact]
    public async System.Threading.Tasks.Task An_explicit_async_conversion_still_throws()
    {
        if (!RequireServer()) return;
        DropBoth();

        var connector = new TimescaleDBConnector(Settings());
        connector.CreateTable(new[] { typeof(GuidKeyedRow) });   // degrades, leaving a plain table

        var act = async () => await connector.CreateHypertableAsync(GuidKeyedTable, "ts");

        (await act.Should().ThrowAsync<Exception>())
            .Where(ex => Flatten(ex).Contains("TS103") || Flatten(ex).Contains("unique index"),
                "the async explicit door must agree with the sync one — degrading is a property of "
              + "schema-ensure, not of the conversion call");
    }

    /// <summary>
    /// <b>Keyed, not logged.</b> TASK-204's own regression was an append-only list: connectors are cached
    /// process-wide while <c>_initialized</c> lives on the store, so a scoped store re-runs schema-ensure per
    /// request and the list grew by one entry per HTTP request, forever. Asserted rather than asserted-by-
    /// construction, because that is precisely what let the original ship.
    /// </summary>
    [Fact]
    public void The_failure_is_recorded_once_however_many_attempts_run()
    {
        if (!RequireServer()) return;
        DropBoth();

        var connector = new TimescaleDBConnector(Settings());
        connector.CreateTable(new[] { typeof(GuidKeyedRow) });
        connector.CreateTable(new[] { typeof(GuidKeyedRow) });
        connector.CreateTable(new[] { typeof(GuidKeyedRow) });

        connector.HypertableCreationFailures.Should().ContainSingle(
            "three schema-ensure runs against one shared connector must leave ONE entry, not three");
    }

    /// <summary>
    /// <b>Transition-fired, not per-attempt.</b> An event on every attempt would fire on every HTTP request
    /// for a per-request store over an unconvertible table — the notification equivalent of the growing list.
    /// </summary>
    [Fact]
    public void The_event_fires_on_the_transition_into_failure_not_on_every_attempt()
    {
        if (!RequireServer()) return;
        DropBoth();

        var connector = new TimescaleDBConnector(Settings());
        var raised = 0;
        connector.OnHypertableCreationFailed += _ => raised++;

        connector.CreateTable(new[] { typeof(GuidKeyedRow) });
        connector.CreateTable(new[] { typeof(GuidKeyedRow) });

        raised.Should().Be(1, "the subscriber is told when the condition begins, not once per attempt");
    }

    /// <summary>
    /// <b>Cleared when repaired.</b> A report that cannot un-report is a report an operator learns to ignore
    /// (§ Conventions). The re-attempt is deliberately kept — that is what lets the conversion succeed on its
    /// own once the blocking constraint is gone, with no restart — so the record must drop out when it does.
    /// </summary>
    [Fact]
    public void The_record_clears_once_the_conversion_succeeds()
    {
        if (!RequireServer()) return;
        DropBoth();

        var connector = new TimescaleDBConnector(Settings());
        connector.CreateTable(new[] { typeof(GuidKeyedRow) });
        connector.HypertableCreationFailures.Should().ContainSingle("precondition: it failed first");

        // Repair the blocking condition exactly as an operator would: the unique/primary key that omits the
        // partitioning column is what TS103 objects to.
        var pk = Scalar("SELECT conname FROM pg_constraint "
                      + $"WHERE conrelid = '\"{GuidKeyedTable}\"'::regclass AND contype = 'p'");
        Exec($"ALTER TABLE \"{GuidKeyedTable}\" DROP CONSTRAINT \"{pk}\"");

        connector.CreateTable(new[] { typeof(GuidKeyedRow) });

        IsHypertable(GuidKeyedTable).Should().BeTrue("the re-attempt is kept, so the repair takes effect");
        connector.HypertableCreationFailures.Should().BeEmpty(
            "current state, not history — the condition an operator repaired must stop being reported");
    }

    /// <summary>
    /// <b>Degrading is conditional, and this is the condition.</b> Inside a caller's ambient boundary the
    /// <c>CREATE TABLE</c> is not committed and the failed <c>create_hypertable</c> aborts the transaction,
    /// so swallowing would report success over a table that will not exist — measured: 0 rows in
    /// <c>pg_tables</c> afterwards, and every later command in that transaction fails with <c>25P02</c>,
    /// naming neither <c>TS103</c> nor the table. The caller would lose the real error entirely.
    /// <para>
    /// Found by <c>code-review</c> at TASK-254's close gate, against a doc comment that had already written
    /// down why it would be worse. The premise had been measured only on the own-connection path — <b>a
    /// premise measured on one path is a sample, not a premise.</b> TASK-244 is what made this path
    /// reachable, by having <c>InitCore</c> enter the ambient scope.
    /// </para>
    /// </summary>
    [Fact]
    public void Inside_an_ambient_boundary_the_failure_is_rethrown_rather_than_degraded()
    {
        if (!RequireServer()) return;
        DropBoth();

        var settings = Settings();
        var connector = new TimescaleDBConnector(settings);

        using var connection = new NpgsqlConnection(settings.GetConnectionString());
        connection.Open();
        using var transaction = connection.BeginTransaction();

        using (AmbientSqlTransaction.Enter(settings.GetId(), connection, transaction))
        {
            var act = () => connector.CreateTable(new[] { typeof(GuidKeyedRow) });

            act.Should().Throw<Exception>(
                "on the boundary path the table does not survive, so degrading would leave the store "
              + "initialised over a table that does not exist — worse than the throw it replaces");
        }

        transaction.Rollback();
    }

    /// <summary>
    /// An absent chunk interval omits the argument rather than emitting <c>INTERVAL ''</c> (which is
    /// <c>22007</c>), matching the sibling emitter in <c>TimescaleDBMigration</c>.
    /// <para>
    /// This matters <i>because</i> schema-ensure now degrades: before TASK-254 a blank
    /// <c>ChunkTimeInterval</c> failed loudly out of <c>CreateTable</c>; afterwards it would be caught and
    /// recorded, so <b>no</b> table on that connector would ever become a hypertable and nothing would
    /// surface unless the consumer had subscribed to the event. The value is reachable — the property has a
    /// public setter and is also fed by the 7-arg constructor and <c>LoadFrom</c>.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void An_absent_chunk_interval_omits_the_argument(string? interval)
    {
        var sql = new TimescaleDBConnector(Settings()!)
            .BuildCreateHypertableSql(GuidKeyedTable, "ts", interval!);

        sql.Should().NotContain("chunk_time_interval",
            "an omitted argument lets TimescaleDB apply its own default, which is what supplying no "
          + "interval means");
        sql.Should().NotContain("INTERVAL ''", "INTERVAL '' is 22007, and would now be silently recorded");
        sql.Should().Contain("if_not_exists => TRUE", "the rest of the statement is unchanged");
    }

    /// <summary>
    /// <b>A subscriber that throws must not defeat the degrade.</b> The event fires inside
    /// <c>CreateTable</c>'s catch, so an escaping handler exception would propagate out of schema-ensure and
    /// leave the store permanently uninitialised — the exact failure this task removes, reintroduced through
    /// the reporting channel. A host that logs and escalates is the realistic trigger, and the event's own
    /// summary invites exactly that.
    /// </summary>
    [Fact]
    public void A_throwing_subscriber_does_not_defeat_the_degrade()
    {
        if (!RequireServer()) return;
        DropBoth();

        var connector = new TimescaleDBConnector(Settings());
        connector.OnHypertableCreationFailed += _ => throw new InvalidOperationException("handler blew up");

        var act = () => connector.CreateTable(new[] { typeof(GuidKeyedRow) });

        act.Should().NotThrow("the caller's handler failing is not a second schema failure");
        connector.HypertableCreationFailures.Should().ContainSingle(
            "and the record still stands — the report is not lost with the handler");
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
