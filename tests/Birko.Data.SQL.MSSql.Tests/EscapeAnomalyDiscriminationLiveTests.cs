using System;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MSSql.Stores;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MSSql.Tests;

/// <summary>
/// TASK-293 — <b>which table an escape is about is decided by the provider's own error, not by a
/// substring search over the statement.</b> See the SQLite suite
/// (<c>EscapeAnomalyDiscriminationTests</c>) for the two false positives that motivated it; both were
/// provider-independent.
///
/// <para>This suite is per provider because the extraction reads the provider's <b>typed</b> exception —
/// a <c>SqlException</c> with error 208 here — which no offline test can produce. The identifier is
/// taken from between the quotes rather than after an English phrase, because a schema qualifier the message may
/// carry has to be stripped: <c>TablesCreated</c> is keyed by the bare framework table name. SQL Server
/// exposes no structured object name on <c>SqlError</c>, so the message is the only source.</para>
/// </summary>
public class EscapeAnomalyDiscriminationLiveTests : IDisposable
{
    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_MSSQL_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MSSQL_PORT"), out var p) ? p : 1433;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_MSSQL_USER") ?? "sa";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_MSSQL_PASSWORD") ?? "Birko!Passw0rd";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_MSSQL_DB") ?? "birkoview";
    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _out;

    public EscapeAnomalyDiscriminationLiveTests(ITestOutputHelper output) => _out = output;

    private bool RequireServer()
    {
        if (!string.IsNullOrWhiteSpace(Host)) return true;
        const string message = "SKIPPED: no live SQL Server. Set BIRKO_MSSQL_HOST to exercise this test; "
                             + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _out.WriteLine(message);
        if (RequireLive) throw new InvalidOperationException(message);
        return false;
    }

    private static MSSqlSettings Settings()
        => new(Host!, Database, User, Password, Port) { TrustServerCertificate = true };

    [Table("MsAnomMovement")]
    public class MsAnomMovement : AbstractDatabaseModel { public string? Value { get; set; } }

    private static void Exec(string sql)
    {
        using var conn = new SqlConnection(Settings().GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(Host)) return;
        foreach (var t in new[] { "MsAnomMovement", "MsAnomLedger" })
        {
            try { Exec($"DROP TABLE IF EXISTS [{t}]"); } catch { }
        }
    }

    /// <summary>
    /// A connector of its own per test — <c>DataBase.GetConnector</c> caches process-wide per
    /// (type, settings id), so a shared instance would carry <c>TablesCreated</c> and
    /// <c>SchemaGeneration</c> from one test into the next.
    /// </summary>
    private static MSSqlConnector FreshConnector() => new(Settings());

    /// <summary>
    /// The claim the fix rests on, measured against this provider's exact wording: the error names the
    /// table that is missing and does <b>not</b> name the one that is fine. A statement mentions both,
    /// which is why it cannot discriminate.
    /// </summary>
    [Fact]
    public void The_error_names_the_missing_table_and_not_the_healthy_one()
    {
        if (!RequireServer()) return;
        Exec("DROP TABLE IF EXISTS [MsAnomLedger]");
        Exec("DROP TABLE IF EXISTS [MsAnomMovement]");
        Exec("CREATE TABLE [MsAnomLedger] ([Guid] NVARCHAR(64))");

        var connector = FreshConnector();
        Exception? caught = null;
        try
        {
            using var conn = new SqlConnection(Settings().GetConnectionString());
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT count(*) FROM [MsAnomLedger] AS MsAnomLedger, "
                + "[MsAnomMovement] AS MsAnomMovement";
            cmd.ExecuteScalar();
        }
        catch (Exception ex) { caught = ex; }

        caught.Should().NotBeNull();
        _out.WriteLine($"number={(caught as SqlException)?.Number} message={caught!.Message}");

        connector.IsMissingTableException(caught).Should().BeTrue();
        connector.MissingTableName(caught).Should().Be("MsAnomMovement",
            "a schema qualifier must be stripped, or the lookup misses the very "
            + "TablesCreated entry it is looking for");
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
        Exec("DROP TABLE IF EXISTS [MsAnomMovement]");

        var connector = FreshConnector();
        connector.CreateTable(new[] { typeof(MsAnomMovement) });

        _out.WriteLine($"created=[{string.Join(", ", connector.TablesCreated.Keys)}]");
        connector.TablesCreated.Keys.Should().Contain("MsAnomMovement",
            "the recording now lives in a non-virtual wrapper this connector's CreateTableCore override "
            + "cannot bypass");

        // And the consequence: a table this connector created and that then vanished is now the ANOMALY
        // here, not a benign first touch.
        Exec("DROP TABLE IF EXISTS [MsAnomMovement]");
        connector.SelectCount(typeof(MsAnomMovement)).Should().Be(0,
            "TASK-285's answer is unchanged — the count is still 0, it is now also RECORDED");
        connector.SchemaEscapes.Should().ContainSingle()
            .Which.TableNames.Should().Contain("MsAnomMovement");
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
        Exec("DROP TABLE IF EXISTS [MsAnomMovement]");

        var store = new AsyncMSSqlStore<MsAnomMovement>();
        store.SetSettings(Settings());
        await store.CreateAsync(new MsAnomMovement { Guid = Guid.NewGuid(), Value = "seed" });

        Exec("DROP TABLE IF EXISTS [MsAnomMovement]");

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

    private static async Task<bool> Attempt(AsyncMSSqlStore<MsAnomMovement> store, string value)
    {
        try
        {
            await store.CreateAsync(new MsAnomMovement { Guid = Guid.NewGuid(), Value = value });
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
