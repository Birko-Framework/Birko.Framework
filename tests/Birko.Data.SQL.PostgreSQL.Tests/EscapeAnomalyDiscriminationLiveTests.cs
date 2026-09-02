using System;
using System.Linq;
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
/// TASK-293 — <b>which table an escape is about is decided by the provider's own error, not by a
/// substring search over the statement.</b>
///
/// <para><c>AbstractConnector</c> calls an escape "the anomaly" when a table it recorded a
/// <c>CREATE TABLE</c> for is reported missing. It used to ask that of the <i>statement</i>: does any
/// recorded name occur as a substring of the SQL? Two false positives followed, both measured on SQLite
/// and neither provider-specific — a recorded <c>Movement</c> made a first touch of <c>StockMovements</c>
/// read as the anomaly, and a statement naming two tables (one created, one not) read as the anomaly on
/// the strength of the created one, which is simply the shape a view or a multi-type count produces.</para>
///
/// <para>Since TASK-288 a false anomaly is not a stray log line: it bumps <c>SchemaGeneration</c> and so
/// invalidates the remembered initialization of <b>every</b> store on the connector, each of which then
/// re-runs its DDL under the connector's lock while its own reads wait.</para>
///
/// <para>This suite is per provider because the extraction reads the provider's <b>typed</b> exception —
/// a <c>PostgresException</c> and its SQLSTATE here — which no offline test can produce. The identifier
/// is taken from between the double quotes rather than after an English phrase, because PostgreSQL
/// localises the prose and never the identifier.</para>
/// </summary>
public class EscapeAnomalyDiscriminationLiveTests : IDisposable
{
    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_PG_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_PG_PORT"), out var p) ? p : 5432;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_PG_USER") ?? "postgres";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_PG_PASSWORD") ?? "postgres";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_PG_DB") ?? "birkoview";
    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _out;

    public EscapeAnomalyDiscriminationLiveTests(ITestOutputHelper output) => _out = output;

    private bool RequireServer()
    {
        if (!string.IsNullOrWhiteSpace(Host)) return true;
        const string message = "SKIPPED: no live PostgreSQL. Set BIRKO_PG_HOST to exercise this test; "
                             + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _out.WriteLine(message);
        if (RequireLive) throw new InvalidOperationException(message);
        return false;
    }

    private static PostgreSqlSettings Settings() => new(Host!, Database, User, Password) { Port = Port };

    [Table("PgAnomMovement")]
    public class PgAnomMovement : AbstractDatabaseModel { public string? Value { get; set; } }

    private static void Exec(string sql)
    {
        using var conn = new NpgsqlConnection(Settings().GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(Host)) return;
        foreach (var t in new[] { "PgAnomMovement", "PgAnomLedger" })
        {
            try { Exec($"DROP TABLE IF EXISTS \"{t}\" CASCADE"); } catch { }
        }
    }

    /// <summary>
    /// A connector of its own per test, so one test's <c>TablesCreated</c> and <c>SchemaGeneration</c>
    /// cannot decide what the next one measures — <c>DataBase.GetConnector</c> caches process-wide per
    /// (type, settings id), so a shared instance would carry both across.
    /// </summary>
    private static PostgreSQLConnector FreshConnector() => new(Settings());

    /// <summary>
    /// The claim the fix rests on, measured against this provider's exact wording rather than taken from
    /// its documentation: the error names the relation that is missing, and does <b>not</b> name the one
    /// that is fine. A statement mentions both, which is why it cannot discriminate.
    /// </summary>
    [Fact]
    public void The_error_names_the_missing_relation_and_not_the_healthy_one()
    {
        if (!RequireServer()) return;
        Exec("DROP TABLE IF EXISTS \"PgAnomLedger\" CASCADE");
        Exec("DROP TABLE IF EXISTS \"PgAnomMovement\" CASCADE");
        Exec("CREATE TABLE \"PgAnomLedger\" (\"Guid\" text)");

        var connector = FreshConnector();
        Exception? caught = null;
        try
        {
            using var conn = new NpgsqlConnection(Settings().GetConnectionString());
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT count(*) FROM \"PgAnomLedger\" AS PgAnomLedger, "
                + "\"PgAnomMovement\" AS PgAnomMovement";
            cmd.ExecuteScalar();
        }
        catch (Exception ex) { caught = ex; }

        caught.Should().NotBeNull();
        _out.WriteLine($"sqlstate={(caught as PostgresException)?.SqlState} message={caught!.Message}");

        connector.IsMissingTableException(caught).Should().BeTrue();
        connector.MissingTableName(caught).Should().Be("PgAnomMovement");
        connector.MissingTableName(caught).Should().NotBe("PgAnomLedger",
            "the healthy relation is named in the statement and must never be the answer");
    }

    /// <summary>
    /// And the shape that is about the STATEMENT rather than a missing relation must yield no name.
    /// <c>42P01</c> is also what PostgreSQL raises for <c>missing FROM-clause entry for table "x"</c>,
    /// where the relation exists perfectly well — TASK-211 excluded it from
    /// <c>IsMissingTableException</c>, and the extractor must not reintroduce it by parsing the quotes of
    /// a message it was never entitled to read.
    /// </summary>
    [Fact]
    public void A_missing_FROM_clause_entry_yields_no_table_name()
    {
        if (!RequireServer()) return;
        Exec("DROP TABLE IF EXISTS \"PgAnomLedger\" CASCADE");
        Exec("CREATE TABLE \"PgAnomLedger\" (\"Guid\" text)");

        var connector = FreshConnector();
        Exception? caught = null;
        try
        {
            using var conn = new NpgsqlConnection(Settings().GetConnectionString());
            conn.Open();
            using var cmd = conn.CreateCommand();
            // "PgAnomLedger" exists; the qualifier is simply not in the FROM list.
            cmd.CommandText = "SELECT \"PgAnomOther\".\"Guid\" FROM \"PgAnomLedger\"";
            cmd.ExecuteScalar();
        }
        catch (Exception ex) { caught = ex; }

        caught.Should().NotBeNull();
        _out.WriteLine($"sqlstate={(caught as PostgresException)?.SqlState} message={caught!.Message}");

        connector.IsMissingTableException(caught).Should().BeFalse(
            "TASK-211: this 42P01 is about the statement, not a missing relation");
        connector.MissingTableName(caught).Should().BeNull(
            "and the extractor is gated on that classification, so it must not hand back a name here — "
            + "a name would make a healthy relation look like the anomaly");
    }

    /// <summary>
    /// <b>TASK-295 — the created table is recorded here now, so the anomaly is observable on this
    /// provider at all.</b>
    ///
    /// <para>⚠ This test was written by [[TASK-293]] asserting the <b>defect</b>: <c>TablesCreated</c> was
    /// permanently <b>empty</b> on this provider, because <c>RecordTableCreated</c> was called from the
    /// base <c>CreateTable(string, IEnumerable&lt;string&gt;)</c> and this connector <b>overrode</b> that
    /// method. TASK-295 inverted it rather than replacing it — the before/after pair on one test is the
    /// record, as TASK-277 did to TASK-244's pin and TASK-265 to TASK-257's.</para>
    ///
    /// <para>What was inert until then, on every provider but SQLite: TASK-286's annotation (always "NO
    /// recorded CREATE TABLE"), TASK-287's <c>SchemaEscapes</c> channel, and TASK-288's healing — so a
    /// table that vanished beneath an initialised store never healed and every write threw until the
    /// process restarted.</para>
    /// </summary>
    [Fact]
    public void TASK295_the_created_table_is_recorded_so_the_anomaly_is_observable_here()
    {
        if (!RequireServer()) return;
        Exec("DROP TABLE IF EXISTS \"PgAnomMovement\" CASCADE");

        var connector = FreshConnector();
        connector.CreateTable(new[] { typeof(PgAnomMovement) });

        _out.WriteLine($"created=[{string.Join(", ", connector.TablesCreated.Keys)}]");
        connector.TablesCreated.Keys.Should().Contain("PgAnomMovement",
            "the recording now lives in a non-virtual wrapper this connector's CreateTableCore override "
            + "cannot bypass");

        // And the consequence: a table this connector created and that then vanished is now the ANOMALY
        // here, not a benign first touch.
        Exec("DROP TABLE IF EXISTS \"PgAnomMovement\" CASCADE");
        connector.SelectCount(typeof(PgAnomMovement)).Should().Be(0,
            "TASK-285's answer is unchanged — the count is still 0, it is now also RECORDED");
        connector.SchemaEscapes.Should().ContainSingle()
            .Which.TableNames.Should().Contain("PgAnomMovement");
        connector.SchemaEscapes.Single().Annotation.Should()
            .Contain("but this connector already created it");
        connector.SchemaGeneration.Should().Be(1, "TASK-288's healing reads this");
    }

    /// <summary>
    /// <b>TASK-295 — and TASK-288's healing therefore works here, which is the outage half.</b>
    /// With the table dropped beneath an initialised store, the failing write must report (TASK-277) and
    /// the <b>next</b> one must succeed. Before this it never did on this provider: the store kept its
    /// remembered <c>_initialized</c> because <c>SchemaGeneration</c> never moved, so every write threw
    /// until the process restarted.
    /// </summary>
    [Fact]
    public async Task TASK295_a_vanished_table_heals_on_the_next_write_here_too()
    {
        if (!RequireServer()) return;
        Exec("DROP TABLE IF EXISTS \"PgAnomMovement\" CASCADE");

        var store = new AsyncPostgreSQLStore<PgAnomMovement>();
        store.SetSettings(Settings());
        await store.CreateAsync(new PgAnomMovement { Guid = Guid.NewGuid(), Value = "seed" });

        Exec("DROP TABLE IF EXISTS \"PgAnomMovement\" CASCADE");

        var first = await Attempt(store, "w1");
        first.Should().BeFalse(
            "the attempt against the missing table is still REPORTED — TASK-277's contract, which healing "
            + "must not buy recovery back by going quiet about");

        var second = await Attempt(store, "w2");
        second.Should().BeTrue(
            "before TASK-295 this provider's SchemaGeneration never moved, so the store trusted its "
            + "remembered initialization forever and w2, w3, w4 ... all threw as well");

        (await store.CountAsync()).Should().Be(1, "w2 landed; the seed went with the dropped table");
    }

    private static async Task<bool> Attempt(AsyncPostgreSQLStore<PgAnomMovement> store, string value)
    {
        try
        {
            await store.CreateAsync(new PgAnomMovement { Guid = Guid.NewGuid(), Value = value });
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
