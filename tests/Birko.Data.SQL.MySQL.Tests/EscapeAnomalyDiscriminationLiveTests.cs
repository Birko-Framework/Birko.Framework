using System;
using System.Linq;
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
    /// ⚠ <b>TASK-295 — the end-to-end anomaly decision cannot be exercised on this provider at all.</b>
    /// <c>RecordTableCreated</c> is called only from the <b>base</b>
    /// <c>CreateTable(string, IEnumerable&lt;string&gt;)</c>, and this provider overrides it without
    /// recording — so <c>TablesCreated</c> is permanently empty here, and with it TASK-286's annotation,
    /// TASK-287's channel and TASK-288's healing.
    /// <para>⚠ Do not "fix" this by adding the call to the three overrides and flipping the assertion:
    /// [[TASK-295]] owns that change, which wants a placement an override cannot bypass rather than a
    /// fourth copy, plus a per-provider before/after measurement.</para>
    /// </summary>
    [Fact]
    public void TASK295_this_provider_records_no_created_tables_so_the_anomaly_is_unobservable_here()
    {
        if (!RequireServer()) return;
        Exec("DROP TABLE IF EXISTS `MyAnomMovement`");

        var connector = FreshConnector();
        connector.CreateTable(new[] { typeof(MyAnomMovement) });

        _out.WriteLine($"created=[{string.Join(", ", connector.TablesCreated.Keys)}]");
        connector.TablesCreated.Should().BeEmpty(
            "when TASK-295 lands this inverts to Contain(\"MyAnomMovement\")");

        Exec("DROP TABLE IF EXISTS `MyAnomMovement`");
        connector.SelectCount(typeof(MyAnomMovement)).Should().Be(0);
        connector.SchemaEscapes.Should().BeEmpty();
        connector.SchemaGeneration.Should().Be(0);
    }
}
