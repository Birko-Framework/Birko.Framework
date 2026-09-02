using System;
using System.Linq;
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
    /// ⚠ <b>TASK-295 — the end-to-end anomaly decision cannot be exercised on this provider at all, and
    /// this records why rather than leaving the gap as prose.</b>
    ///
    /// <para><c>AbstractConnector.RecordTableCreated</c> is called from exactly one place: the <b>base</b>
    /// <c>CreateTable(string, IEnumerable&lt;string&gt;)</c>. PostgreSQL, MySQL and SQL Server each
    /// <b>override</b> that method with their own emitter and none of them records — so
    /// <c>TablesCreated</c> is permanently empty on three of the four providers, and with it TASK-286's
    /// annotation (always "NO recorded CREATE TABLE"), TASK-287's <c>SchemaEscapes</c> channel and
    /// TASK-288's healing. The whole escape/heal apparatus is SQLite-only in practice.</para>
    ///
    /// <para>Fifth instance of this repo's own <i>"a funnel with four overrides is not a funnel"</i>
    /// (TASK-215, TASK-242, TASK-243, TASK-245).</para>
    ///
    /// <para>⚠ <b>Do not "fix" this by adding the call to the three overrides and flipping the
    /// assertion.</b> [[TASK-295]] owns the shape of that change — the recording wants a placement an
    /// override cannot bypass, not a fourth copy — and it needs its own before/after measurement per
    /// provider. TASK-293's claim, that the discriminator reads the provider error rather than the
    /// statement, is asserted above and does not depend on this.</para>
    /// </summary>
    [Fact]
    public void TASK295_this_provider_records_no_created_tables_so_the_anomaly_is_unobservable_here()
    {
        if (!RequireServer()) return;
        Exec("DROP TABLE IF EXISTS \"PgAnomMovement\" CASCADE");

        var connector = FreshConnector();
        connector.CreateTable(new[] { typeof(PgAnomMovement) });

        _out.WriteLine($"created=[{string.Join(", ", connector.TablesCreated.Keys)}]");
        connector.TablesCreated.Should().BeEmpty(
            "PostgreSQLConnector overrides CreateTable(string, IEnumerable<string>) and does not call "
            + "RecordTableCreated. When TASK-295 lands, this inverts to Contain(\"PgAnomMovement\")");

        // The consequence, measured rather than inferred: with nothing recorded, a table this connector
        // really did create and that really did vanish reads as a benign first touch.
        Exec("DROP TABLE IF EXISTS \"PgAnomMovement\" CASCADE");
        connector.SelectCount(typeof(PgAnomMovement)).Should().Be(0);
        connector.SchemaEscapes.Should().BeEmpty();
        connector.SchemaGeneration.Should().Be(0);
    }
}
