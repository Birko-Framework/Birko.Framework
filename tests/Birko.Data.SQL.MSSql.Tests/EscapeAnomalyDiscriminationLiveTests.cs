using System;
using System.Linq;
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
        Exec("DROP TABLE IF EXISTS [MsAnomMovement]");

        var connector = FreshConnector();
        connector.CreateTable(new[] { typeof(MsAnomMovement) });

        _out.WriteLine($"created=[{string.Join(", ", connector.TablesCreated.Keys)}]");
        connector.TablesCreated.Should().BeEmpty(
            "when TASK-295 lands this inverts to Contain(\"MsAnomMovement\")");

        Exec("DROP TABLE IF EXISTS [MsAnomMovement]");
        connector.SelectCount(typeof(MsAnomMovement)).Should().Be(0);
        connector.SchemaEscapes.Should().BeEmpty();
        connector.SchemaGeneration.Should().Be(0);
    }
}
