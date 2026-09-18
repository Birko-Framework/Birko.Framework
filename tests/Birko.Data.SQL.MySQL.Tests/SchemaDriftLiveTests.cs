using System;
using System.Linq;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SchemaDrift;
using Birko.Data.SQL.MySQL.Stores;
using FluentAssertions;
using MySqlConnector;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MySQL.Tests;

/// <summary>
/// TASK-269 — the schema-drift check on MySQL, against a live server.
///
/// <para>
/// MySQL is the provider where the catalogue answer is closest to free: <c>COLUMN_TYPE</c> carries the
/// type verbatim with its parameters and modifiers, which is already <c>ConvertType</c>'s vocabulary.
/// What still needs a server is that this is true of the shapes this framework actually emits —
/// <c>int</c> vs <c>int unsigned</c>, <c>tinyint(1)</c> for a bool, <c>longtext</c> for an unlengthed
/// string, <c>char(36)</c> for a Guid.
/// </para>
///
/// <para>Gated on <c>BIRKO_MYSQL_HOST</c>; set <c>BIRKO_REQUIRE_LIVE</c> to make its absence a failure.</para>
/// </summary>
public class SchemaDriftLiveTests
{
    private const string CleanTable = "MyDriftClean";
    private const string DriftedTable = "MyDriftStale";

    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_MYSQL_HOST");
    private static int Port => uint.TryParse(Environment.GetEnvironmentVariable("BIRKO_MYSQL_PORT"), out var p) ? (int)p : 3306;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_MYSQL_USER") ?? "root";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_MYSQL_PASSWORD") ?? "root";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_MYSQL_DB") ?? "birkoview";
    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _output;

    public SchemaDriftLiveTests(ITestOutputHelper output) => _output = output;

    private bool RequireServer()
    {
        if (!string.IsNullOrWhiteSpace(Host)) return true;
        const string message = "SKIPPED: no live MySQL. Set BIRKO_MYSQL_HOST to exercise this test; "
                             + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (RequireLive) throw new InvalidOperationException(message);
        return false;
    }

    private static MySqlSettings Settings() => new(Host!, Database, User, Password, Port);

    [Table(CleanTable)]
    public class CleanRow
    {
        [PrimaryField]
        public Guid? Guid { get; set; }
        public string? Name { get; set; }
        public int Amount { get; set; }
        public bool Flag { get; set; }
        public DateTime Seen { get; set; }
        [PrecisionField(18)]
        [ScaleField(2)]
        public decimal Money { get; set; }
    }

    [Table(DriftedTable)]
    public class DriftedRow
    {
        [PrimaryField]
        public Guid? Guid { get; set; }
        [PrecisionField(18)]
        [ScaleField(2)]
        public decimal Money { get; set; }
    }

    private static void Exec(string sql)
    {
        using var conn = new MySqlConnection(Settings().GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// The control that validates the whole vocabulary at once: if <c>COLUMN_TYPE</c> disagreed with
    /// <c>ConvertType</c> for any of bool, Guid, unlengthed string, int or decimal, this goes red on
    /// the untouched column — the false-positive direction, which is the one that matters.
    /// </summary>
    [Fact]
    public void A_table_the_framework_created_reports_clean()
    {
        if (!RequireServer()) return;

        Exec($"DROP TABLE IF EXISTS `{CleanTable}`");
        var connector = new MySQLConnector(Settings());
        connector.CreateTable(new[] { typeof(CleanRow) });

        var report = connector.DetectDrift(typeof(CleanRow));
        foreach (var d in report.Drifts) _output.WriteLine(d.ToString());

        report.Supported.Should().BeTrue();
        report.TableExists.Should().BeTrue();
        report.Drifts.Should().BeEmpty();
    }

    /// <summary>
    /// ⚠ TASK-264's case, on a server: <c>decimal(18,0)</c> and <c>decimal(18,2)</c> are the same
    /// <c>DATA_TYPE</c>. This is why the query reads <c>COLUMN_TYPE</c> — the bare keyword would report
    /// silent money truncation as healthy.
    /// </summary>
    [Fact]
    public void A_difference_only_in_SCALE_is_reported()
    {
        if (!RequireServer()) return;

        Exec($"DROP TABLE IF EXISTS `{DriftedTable}`");
        Exec($"CREATE TABLE `{DriftedTable}` (`Guid` char(36), `Money` decimal(18,0))");

        var report = new MySQLConnector(Settings()).DetectDrift(typeof(DriftedRow));

        var drift = report.Drifts.Should().ContainSingle().Subject;
        drift.Column.Should().Be("Money");
        drift.Kind.Should().Be(ColumnDriftKind.TypeMismatch);
        drift.Stored.Should().Be("decimal(18,0)");
        drift.Declared.Should().Be("DECIMAL(18,2)");
    }

    /// <summary>
    /// The query is scoped to <c>DATABASE()</c>. Without it a server hosting a same-named table in a
    /// second schema returns both, and the answer becomes whichever row arrived last.
    /// </summary>
    [Fact]
    public void A_same_named_table_in_another_schema_is_not_consulted()
    {
        if (!RequireServer()) return;

        Exec($"DROP TABLE IF EXISTS `{DriftedTable}`");
        Exec($"CREATE TABLE `{DriftedTable}` (`Guid` char(36), `Money` decimal(18,2))");

        Exec("CREATE DATABASE IF NOT EXISTS birkodrift_other");
        try
        {
            Exec($"DROP TABLE IF EXISTS birkodrift_other.`{DriftedTable}`");
            Exec($"CREATE TABLE birkodrift_other.`{DriftedTable}` (`Guid` char(36), `Money` decimal(18,0), `Extra` text)");

            var report = new MySQLConnector(Settings()).DetectDrift(typeof(DriftedRow));
            foreach (var d in report.Drifts) _output.WriteLine(d.ToString());

            report.Drifts.Should().BeEmpty(
                "the other schema's stale table and its extra column must not be consulted");
        }
        finally
        {
            Exec("DROP DATABASE IF EXISTS birkodrift_other");
        }
    }
}
