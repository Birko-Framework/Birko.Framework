using System;
using System.Linq;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SchemaDrift;
using Birko.Data.SQL.MSSql.Stores;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MSSql.Tests;

/// <summary>
/// TASK-269 — the schema-drift check on SQL Server, against a live server.
///
/// <para>
/// ⚠ <b>This is the provider whose rendering is most likely to be wrong, and only a server can say.</b>
/// <c>sys.columns.max_length</c> is in <b>bytes</b>, and for an <c>N</c>-prefixed type that is twice the
/// declared character count — so a column declared <c>NVARCHAR(255)</c> reports 510. Getting the halving
/// wrong reports every bounded string column in the database as drifted, which is a false positive on
/// every entity and worse than the hole it closes (§ Conventions' <c>PredicateScope</c> rule). The
/// <c>-1</c>-means-MAX case must not be halved either.
/// </para>
///
/// <para>Gated on <c>BIRKO_MSSQL_HOST</c>; set <c>BIRKO_REQUIRE_LIVE</c> to make its absence a failure.</para>
/// </summary>
public class SchemaDriftLiveTests
{
    private const string CleanTable = "MsDriftClean";
    private const string DriftedTable = "MsDriftStale";

    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_MSSQL_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MSSQL_PORT"), out var p) ? p : 1433;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_MSSQL_USER") ?? "sa";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_MSSQL_PASSWORD") ?? "Birko!Passw0rd";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_MSSQL_DB") ?? "birkoview";
    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _output;

    public SchemaDriftLiveTests(ITestOutputHelper output) => _output = output;

    private bool RequireServer()
    {
        if (!string.IsNullOrWhiteSpace(Host)) return true;
        const string message = "SKIPPED: no live SQL Server. Set BIRKO_MSSQL_HOST to exercise this test; "
                             + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (RequireLive) throw new InvalidOperationException(message);
        return false;
    }

    private static MSSqlSettings Settings() => new(Host!, Database, User, Password, Port)
    {
        TrustServerCertificate = true
    };

    /// <summary>
    /// Carries every width shape at once: an unlengthed string (NVARCHAR(MAX) since TASK-257), a
    /// bounded one (NVARCHAR(n), the halving case), unbounded and bounded binary (TASK-266), and a
    /// decimal (precision/scale).
    /// </summary>
    [Table(CleanTable)]
    public class CleanRow
    {
        [PrimaryField]
        public Guid? Guid { get; set; }
        public string? Unlengthed { get; set; }
        [MaxLengthField(255)]
        public string? Bounded { get; set; }
        public byte[]? Blob { get; set; }
        [MaxLengthField(32)]
        public byte[]? BoundedBlob { get; set; }
        public int Amount { get; set; }
        [PrecisionField(18)]
        [ScaleField(2)]
        public decimal Money { get; set; }
    }

    [Table(DriftedTable)]
    public class DriftedRow
    {
        [PrimaryField]
        public Guid? Guid { get; set; }
        [MaxLengthField(255)]
        public string? Bounded { get; set; }
    }

    private static void Exec(string sql)
    {
        using var conn = new SqlConnection(Settings().GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// The control, and the one that catches a wrong halving: every column here was created by
    /// <c>ConvertType</c>, so a correct renderer reports nothing. A renderer that forgot to halve
    /// reports <c>Bounded</c> as <c>NVARCHAR(510)</c> against a declared <c>NVARCHAR(255)</c>.
    /// </summary>
    [Fact]
    public void A_table_the_framework_created_reports_clean()
    {
        if (!RequireServer()) return;

        Exec($"DROP TABLE IF EXISTS [{CleanTable}]");
        var connector = new MSSqlConnector(Settings());
        connector.CreateTable(new[] { typeof(CleanRow) });

        var report = connector.DetectDrift(typeof(CleanRow));
        foreach (var d in report.Drifts) _output.WriteLine(d.ToString());

        report.Supported.Should().BeTrue();
        report.TableExists.Should().BeTrue();
        report.Drifts.Should().BeEmpty(
            "max_length is in bytes and an N-type must be halved; an unhalved renderer reports every "
            + "bounded string as drifted");
    }

    /// <summary>
    /// TASK-257's own case, which is what this whole task was spawned to make visible: a database
    /// created before that fix keeps its <c>TEXT</c> column, and every string predicate against it
    /// raises Msg 402 at the call site with nothing else to say so.
    /// </summary>
    [Fact]
    public void A_pre_TASK257_TEXT_column_is_reported_against_the_NVARCHAR_the_model_now_declares()
    {
        if (!RequireServer()) return;

        Exec($"DROP TABLE IF EXISTS [{DriftedTable}]");
        Exec($"CREATE TABLE [{DriftedTable}] ([Guid] UNIQUEIDENTIFIER, [Bounded] TEXT)");

        var report = new MSSqlConnector(Settings()).DetectDrift(typeof(DriftedRow));

        var drift = report.Drifts.Should().ContainSingle().Subject;
        drift.Column.Should().Be("Bounded");
        drift.Kind.Should().Be(ColumnDriftKind.TypeMismatch);
        drift.Stored.Should().Be("TEXT");
        drift.Declared.Should().Be("NVARCHAR(255)");
    }

    /// <summary>
    /// The MAX case must not be halved: <c>max_length</c> is <c>-1</c>, and <c>-1/2</c> is a width of
    /// zero, which would report every unlengthed string and every blob as drifted.
    /// </summary>
    [Fact]
    public void A_MAX_width_is_rendered_as_MAX_and_not_halved()
    {
        if (!RequireServer()) return;

        Exec($"DROP TABLE IF EXISTS [{DriftedTable}]");
        Exec($"CREATE TABLE [{DriftedTable}] ([Guid] UNIQUEIDENTIFIER, [Bounded] NVARCHAR(MAX))");

        var report = new MSSqlConnector(Settings()).DetectDrift(typeof(DriftedRow));

        var drift = report.Drifts.Should().ContainSingle().Subject;
        drift.Stored.Should().Be("NVARCHAR(MAX)");
        drift.Declared.Should().Be("NVARCHAR(255)");
    }

    /// <summary>TASK-511: an unprecisioned decimal, which <c>ConvertType</c> emits as a bare <c>DECIMAL</c>.</summary>
    [Table(BareDecimalTable)]
    public class BareDecimalRow
    {
        [PrimaryField]
        public Guid? Guid { get; set; }
        public decimal Amount { get; set; }
    }

    private const string BareDecimalTable = "MsDriftBareDecimal";

    private static decimal AmountOf(MSSqlConnector connector, Guid guid)
        => connector.Select(typeof(BareDecimalRow), (System.Linq.Expressions.LambdaExpression?)null)
                    .Cast<BareDecimalRow>().Single(x => x.Guid == guid).Amount;

    /// <summary>
    /// TASK-512. SQL Server stores a bare <c>DECIMAL</c> as <c>DECIMAL(18,0)</c> — scale 0, so <c>7.5</c> was
    /// stored as <c>8</c> with no error. An unprecisioned decimal is now <c>DECIMAL(22,6)</c>.
    /// </summary>
    [Fact]
    public void An_unprecisioned_decimal_keeps_its_fraction()
    {
        if (!RequireServer()) return;

        Exec($"DROP TABLE IF EXISTS [{BareDecimalTable}]");
        var connector = new MSSqlConnector(Settings());
        connector.CreateTable(new[] { typeof(BareDecimalRow) });
        var half = Guid.NewGuid();
        var six = Guid.NewGuid();
        connector.Insert(new BareDecimalRow { Guid = half, Amount = 7.5m });
        connector.Insert(new BareDecimalRow { Guid = six, Amount = 1234.567891m });

        AmountOf(connector, half).Should().Be(7.5m, "DECIMAL(18,0) rounded this to 8");
        AmountOf(connector, six).Should().Be(1234.567891m, "six places is the canonical scale");
    }

    /// <summary>
    /// The column the framework now creates reports clean — TASK-511 made this pass by canonicalising the
    /// bare declaration in the comparison; TASK-512 removed that, because nothing declares a bare one any more.
    /// </summary>
    [Fact]
    public void An_unprecisioned_decimal_the_framework_created_reports_clean()
    {
        if (!RequireServer()) return;

        Exec($"DROP TABLE IF EXISTS [{BareDecimalTable}]");
        var connector = new MSSqlConnector(Settings());
        connector.CreateTable(new[] { typeof(BareDecimalRow) });

        var report = connector.DetectDrift(typeof(BareDecimalRow));
        foreach (var d in report.Drifts) _output.WriteLine(d.ToString());

        report.IsClean.Should().BeTrue();
    }

    /// <summary>
    /// TASK-511's "a bare column reports clean", INVERTED (rule 56): a table created before TASK-512 holds an
    /// integer column, and that is exactly what an operator needs to be told — it is losing every fraction.
    /// </summary>
    [Fact]
    public void A_table_created_before_TASK512_reports_its_integer_decimal_as_drift()
    {
        if (!RequireServer()) return;

        Exec($"DROP TABLE IF EXISTS [{BareDecimalTable}]");
        Exec($"CREATE TABLE [{BareDecimalTable}] ([Guid] UNIQUEIDENTIFIER, [Amount] DECIMAL NOT NULL)");

        var drift = new MSSqlConnector(Settings()).DetectDrift(typeof(BareDecimalRow))
            .Drifts.Should().ContainSingle().Subject;
        drift.Column.Should().Be("Amount");
        drift.Kind.Should().Be(ColumnDriftKind.TypeMismatch);
        drift.Declared.Should().Be("DECIMAL(22,6)");
        drift.Stored.Should().Be("DECIMAL(18,0)");
    }

    /// <summary>
    /// The migration CHANGELOG.md gives for such a table, proven: it clears the drift and the column keeps a
    /// fraction afterwards. Values written before it stay rounded — the fraction was never stored.
    /// </summary>
    [Fact]
    public void The_documented_migration_widens_an_old_column()
    {
        if (!RequireServer()) return;

        Exec($"DROP TABLE IF EXISTS [{BareDecimalTable}]");
        Exec($"CREATE TABLE [{BareDecimalTable}] ([Guid] UNIQUEIDENTIFIER, [Amount] DECIMAL NOT NULL)");
        var connector = new MSSqlConnector(Settings());
        var old = Guid.NewGuid();
        connector.Insert(new BareDecimalRow { Guid = old, Amount = 7.5m });

        Exec($"ALTER TABLE [{BareDecimalTable}] ALTER COLUMN [Amount] DECIMAL(22,6) NOT NULL");

        connector.DetectDrift(typeof(BareDecimalRow)).IsClean.Should().BeTrue();
        AmountOf(connector, old).Should().Be(8m, "the fraction was rounded away on write and cannot come back");
        var fresh = Guid.NewGuid();
        connector.Insert(new BareDecimalRow { Guid = fresh, Amount = 7.5m });
        AmountOf(connector, fresh).Should().Be(7.5m);
    }
}
