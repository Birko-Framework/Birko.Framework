using System;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MySQL.Stores;
using FluentAssertions;
using MySqlConnector;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MySQL.Tests;

/// <summary>
/// TASK-293 — <b>which table an escape is about is decided by the provider's own error, not by a
/// substring search over the statement.</b> See the SQLite suite
/// (<c>EscapeAnomalyDiscriminationTests</c>) for the two false positives that motivated it; both were
/// provider-independent.
///
/// <para>This suite is per provider because the extraction reads the provider's <b>typed</b> exception —
/// a <c>MySqlException</c> with error 1146 here — which no offline test can produce. The identifier is
/// taken from between the quotes rather than after an English phrase, because MySQL localises the prose
/// and never the identifier, and the <c>database.</c> qualifier it includes has to be stripped:
/// <c>TablesCreated</c> is keyed by the bare framework table name.</para>
/// </summary>
public class EscapeAnomalyDiscriminationLiveTests : IDisposable
{
    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_MYSQL_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MYSQL_PORT"), out var p) ? p : 3306;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_MYSQL_USER") ?? "root";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_MYSQL_PASSWORD") ?? "root";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_MYSQL_DB") ?? "birkoview";
    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _out;

    public EscapeAnomalyDiscriminationLiveTests(ITestOutputHelper output) => _out = output;

    private bool RequireServer()
    {
        if (!string.IsNullOrWhiteSpace(Host)) return true;
        const string message = "SKIPPED: no live MySQL. Set BIRKO_MYSQL_HOST to exercise this test; "
                             + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _out.WriteLine(message);
        if (RequireLive) throw new InvalidOperationException(message);
        return false;
    }

    private static MySqlSettings Settings() => new(Host!, Database, User, Password, Port);

    [Table("MyAnomMovement")]
    public class MyAnomMovement : AbstractDatabaseModel { public string? Value { get; set; } }

    private static void Exec(string sql)
    {
        using var conn = new MySqlConnection(Settings().GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(Host)) return;
        foreach (var t in new[] { "MyAnomMovement", "MyAnomLedger" })
        {
            try { Exec($"DROP TABLE IF EXISTS `{t}`"); } catch { }
        }
    }

    /// <summary>
    /// A connector of its own per test — <c>DataBase.GetConnector</c> caches process-wide per
    /// (type, settings id), so a shared instance would carry <c>TablesCreated</c> and
    /// <c>SchemaGeneration</c> from one test into the next.
    /// </summary>
    private static MySQLConnector FreshConnector() => new(Settings());

    /// <summary>
    /// The claim the fix rests on, measured against this provider's exact wording: the error names the
    /// table that is missing and does <b>not</b> name the one that is fine. A statement mentions both,
    /// which is why it cannot discriminate.
    /// </summary>
    [Fact]
    public void The_error_names_the_missing_table_and_not_the_healthy_one()
    {
        if (!RequireServer()) return;
        Exec("DROP TABLE IF EXISTS `MyAnomLedger`");
        Exec("DROP TABLE IF EXISTS `MyAnomMovement`");
        Exec("CREATE TABLE `MyAnomLedger` (`Guid` VARCHAR(64))");

        var connector = FreshConnector();
        Exception? caught = null;
        try
        {
            using var conn = new MySqlConnection(Settings().GetConnectionString());
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT count(*) FROM `MyAnomLedger` AS MyAnomLedger, "
                + "`MyAnomMovement` AS MyAnomMovement";
            cmd.ExecuteScalar();
        }
        catch (Exception ex) { caught = ex; }

        caught.Should().NotBeNull();
        _out.WriteLine($"code={(caught as MySqlException)?.ErrorCode} message={caught!.Message}");

        connector.IsMissingTableException(caught).Should().BeTrue();
        connector.MissingTableName(caught).Should().Be("MyAnomMovement",
            "the database qualifier MySQL includes must be stripped, or the lookup misses the very "
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
        Exec("DROP TABLE IF EXISTS `MyAnomMovement`");

        var connector = FreshConnector();
        connector.CreateTable(new[] { typeof(MyAnomMovement) });

        _out.WriteLine($"created=[{string.Join(", ", connector.TablesCreated.Keys)}]");
        connector.TablesCreated.Keys.Should().Contain("MyAnomMovement",
            "the recording now lives in a non-virtual wrapper this connector's CreateTableCore override "
            + "cannot bypass");

        // And the consequence: a table this connector created and that then vanished is now the ANOMALY
        // here, not a benign first touch.
        Exec("DROP TABLE IF EXISTS `MyAnomMovement`");
        connector.SelectCount(typeof(MyAnomMovement)).Should().Be(0,
            "TASK-285's answer is unchanged — the count is still 0, it is now also RECORDED");
        connector.SchemaEscapes.Should().ContainSingle()
            .Which.TableNames.Should().Contain("MyAnomMovement");
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
        Exec("DROP TABLE IF EXISTS `MyAnomMovement`");

        var store = new AsyncMySQLStore<MyAnomMovement>();
        store.SetSettings(Settings());
        await store.CreateAsync(new MyAnomMovement { Guid = Guid.NewGuid(), Value = "seed" });

        Exec("DROP TABLE IF EXISTS `MyAnomMovement`");

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

    private static async Task<bool> Attempt(AsyncMySQLStore<MyAnomMovement> store, string value)
    {
        try
        {
            await store.CreateAsync(new MyAnomMovement { Guid = Guid.NewGuid(), Value = value });
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
