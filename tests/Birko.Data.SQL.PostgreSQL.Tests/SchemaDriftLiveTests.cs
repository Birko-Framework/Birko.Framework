using System;
using System.Data.Common;
using System.Linq;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SchemaDrift;
using Birko.Data.SQL.PostgreSQL.Stores;
using FluentAssertions;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.PostgreSQL.Tests;

/// <summary>
/// TASK-269 — the schema-drift check on PostgreSQL, against a live server.
///
/// <para>
/// The rendering here is the part that cannot be checked offline. <c>format_type</c> reports
/// PostgreSQL's own preferred spelling — <c>character varying(255)</c>, <c>timestamp without time
/// zone</c> — which is <b>not</b> what <c>ConvertType</c> emits, so a canonicalisation map stands
/// between the catalogue and the comparison. Every entry in it is wrong until a server says otherwise:
/// a wrong entry makes an untouched column report as drifted, which is a false positive on every
/// entity and worse than the hole (§ Conventions' <c>PredicateScope</c> rule).
/// </para>
///
/// <para>Gated on <c>BIRKO_PG_HOST</c>; set <c>BIRKO_REQUIRE_LIVE</c> to make its absence a failure.</para>
/// </summary>
public class SchemaDriftLiveTests
{
    private const string CleanTable = "PgDriftClean";
    private const string DriftedTable = "PgDriftStale";

    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_PG_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_PG_PORT"), out var p) ? p : 5432;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_PG_USER") ?? "postgres";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_PG_PASSWORD") ?? "postgres";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_PG_DB") ?? "birkoview";
    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _output;

    public SchemaDriftLiveTests(ITestOutputHelper output) => _output = output;

    private bool RequireServer()
    {
        if (!string.IsNullOrWhiteSpace(Host)) return true;
        const string message = "SKIPPED: no live PostgreSQL. Set BIRKO_PG_HOST to exercise this test; "
                             + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (RequireLive) throw new InvalidOperationException(message);
        return false;
    }

    private static PostgreSqlSettings Settings() => new(Host!, Database, User, Password) { Port = Port };

    /// <summary>PascalCase on purpose: a lower-case name would pass whatever the regclass literal did.</summary>
    [Table(CleanTable)]
    public class CleanRow
    {
        [PrimaryField]
        public Guid? Guid { get; set; }
        public string? Name { get; set; }
        public int Amount { get; set; }
        public DateTime Seen { get; set; }
    }

    [Table(DriftedTable)]
    public class DriftedRow
    {
        [PrimaryField]
        public Guid? Guid { get; set; }
        public string? Name { get; set; }
        public int Amount { get; set; }
    }

    private static void Exec(string sql)
    {
        using var conn = new NpgsqlConnection(Settings().GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// The control that makes every other assertion meaningful: a table this framework created must
    /// report clean. If the canonicalisation map is wrong anywhere, this is what goes red — and it goes
    /// red on the untouched columns, which is the false-positive direction.
    /// </summary>
    [Fact]
    public void A_table_the_framework_created_reports_clean()
    {
        if (!RequireServer()) return;

        Exec($"DROP TABLE IF EXISTS \"{CleanTable}\"");
        var connector = new PostgreSQLConnector(Settings());
        connector.CreateTable(new[] { typeof(CleanRow) });

        var report = connector.DetectDrift(typeof(CleanRow));

        foreach (var d in report.Drifts) _output.WriteLine(d.ToString());

        report.Supported.Should().BeTrue();
        report.TableExists.Should().BeTrue();
        report.Drifts.Should().BeEmpty("every column was created by ConvertType, so nothing can disagree with it");
        report.IsClean.Should().BeTrue();
    }

    /// <summary>
    /// ⚠ The regclass literal has to be QUOTED, or a PascalCase table folds and the catalogue query
    /// finds nothing — which would report every column of every entity as Missing rather than failing
    /// loudly. Same identifier family as TASK-472, arriving at a new sink.
    /// </summary>
    [Fact]
    public void A_PascalCase_table_is_found_at_all()
    {
        if (!RequireServer()) return;

        Exec($"DROP TABLE IF EXISTS \"{CleanTable}\"");
        var connector = new PostgreSQLConnector(Settings());
        connector.CreateTable(new[] { typeof(CleanRow) });

        connector.DetectDrift(typeof(CleanRow)).TableExists
                 .Should().BeTrue("a bare regclass would fold to lower case and match nothing");
    }

    [Fact]
    public void A_column_whose_stored_type_is_stale_is_reported()
    {
        if (!RequireServer()) return;

        // What an old database looks like: Amount was widened in the model, the column never was.
        Exec($"DROP TABLE IF EXISTS \"{DriftedTable}\"");
        Exec($"CREATE TABLE \"{DriftedTable}\" (\"Guid\" uuid, \"Name\" text, \"Amount\" text)");

        var report = new PostgreSQLConnector(Settings()).DetectDrift(typeof(DriftedRow));

        var drift = report.Drifts.Should().ContainSingle().Subject;
        drift.Column.Should().Be("Amount");
        drift.Kind.Should().Be(ColumnDriftKind.TypeMismatch);
        drift.Stored.Should().Be("TEXT");
        drift.Declared.Should().Be("INTEGER");
    }

    /// <summary>
    /// ⚠ <c>pg_attribute</c> carries the system columns at negative <c>attnum</c> and keeps dropped
    /// columns as tombstones. Without the filter every table reports several Unexpected columns it does
    /// not have — noise on every entity from the first run, which is how a report gets ignored.
    /// </summary>
    [Fact]
    public void System_columns_and_dropped_columns_are_not_reported()
    {
        if (!RequireServer()) return;

        Exec($"DROP TABLE IF EXISTS \"{DriftedTable}\"");
        Exec($"CREATE TABLE \"{DriftedTable}\" (\"Guid\" uuid, \"Name\" text, \"Amount\" integer, \"Gone\" text)");
        Exec($"ALTER TABLE \"{DriftedTable}\" DROP COLUMN \"Gone\"");

        var report = new PostgreSQLConnector(Settings()).DetectDrift(typeof(DriftedRow));

        foreach (var d in report.Drifts) _output.WriteLine(d.ToString());

        report.Drifts.Should().BeEmpty(
            "a dropped column's tombstone and the system columns must not read as Unexpected");
    }
}
