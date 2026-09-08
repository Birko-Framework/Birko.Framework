using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SchemaDrift;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Health;
using Birko.Health.Data.SQL;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Birko.Health.Data.SQL.Tests;

/// <summary>
/// TASK-269 — nothing reported a column whose stored type no longer matched what the model declares.
///
/// <para>
/// The framework never reconciles an existing table: <c>CREATE TABLE</c> is guarded by
/// <c>IF NOT EXISTS</c> and schema-ensure only creates. So a model change — or an upgrade past one of
/// the column-typing fixes (TASK-257, TASK-264, TASK-265, TASK-266, TASK-275) — leaves the old column in
/// place, and until this existed the only signal was an exception on whichever request first touched it.
/// </para>
///
/// <para>
/// <b>Why these tests run on SQLite.</b> Measured at Step 0: <c>Symbio.Api/appsettings.json</c> reads
/// <c>"Default": "SQLite"</c> in every environment and every non-test <c>DataProvider.MsSql</c>
/// reference across all 16 consumer repos is a switch case in a factory, so SQLite is the only provider
/// with a live population and the only one where drift can already exist in the field. It also needs no
/// server, so the mechanism is provable offline.
/// </para>
///
/// <para>
/// ⚠ <b>The declared side deliberately comes from <c>ConvertType</c>, the method <c>CREATE TABLE</c>
/// itself uses.</b> That is why these tests never spell out an expected SQL type by hand for the healthy
/// case: a test that restated the mapping would be a second implementation of the rule, and would keep
/// passing while the DDL and the check drifted apart.
/// </para>
/// </summary>
public class SchemaDriftEndToEndTests : IDisposable
{
    private readonly string _root;
    private readonly List<string> _connectionStrings = new();
    private static int _seq;

    public SchemaDriftEndToEndTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"birko-drift-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        // TASK-276 -- precise, never process-wide: a process-wide clear disposes the sqlite3 handle a
        // PARALLEL sibling class is mid-statement on, and the victim is never the file that called it.
        foreach (var cs in _connectionStrings)
        {
            try { using var probe = new SqliteConnection(cs); SqliteConnection.ClearPool(probe); } catch { }
        }
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    [Birko.Data.SQL.Attributes.Table("Widget")]
    public class Widget : AbstractModel
    {
        public string? Name { get; set; }
        public int Amount { get; set; }
    }

    /// <summary>
    /// Records each database's connection string so <see cref="Dispose"/> can clear exactly this
    /// fixture's pools -- the pool key is the whole connection string, so nothing here has to guess
    /// (TASK-276).
    /// </summary>
    private SqLiteSettings NewDatabase()
    {
        var settings = new SqLiteSettings(_root, $"drift{Interlocked.Increment(ref _seq)}.db") { CommandTimeout = 5 };
        _connectionStrings.Add(settings.GetConnectionString());
        return settings;
    }

    private static SqLiteConnector Connector(SqLiteSettings settings) => new SqLiteConnector(settings);

    /// <summary>
    /// Creates the table by hand, so a column can be given a type the framework would never emit.
    /// This is what an old database looks like after the model, or the framework's mapping, moved on.
    /// </summary>
    private static void CreateTableWithRawDdl(SqLiteSettings settings, string ddl)
    {
        using var conn = new SqliteConnection(settings.GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = ddl;
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public void A_table_the_framework_just_created_has_no_drift()
    {
        var settings = NewDatabase();
        var connector = Connector(settings);
        connector.CreateTable(new[] { typeof(Widget) });

        var report = connector.DetectDrift(typeof(Widget));

        report.Supported.Should().BeTrue();
        report.TableExists.Should().BeTrue();
        report.Drifts.Should().BeEmpty();
        report.IsClean.Should().BeTrue();
    }

    /// <summary>
    /// The case the task exists for. A stored column of the wrong type is reported, with both sides
    /// named so an operator can write the <c>ALTER</c>.
    /// </summary>
    [Fact]
    public void A_column_whose_stored_type_is_not_the_declared_one_is_reported()
    {
        var settings = NewDatabase();
        // "Amount" is declared INTEGER by the framework; this database has it as TEXT.
        CreateTableWithRawDdl(settings,
            @"CREATE TABLE ""Widget"" (Guid TEXT, Name TEXT, Amount TEXT)");

        var report = Connector(settings).DetectDrift(typeof(Widget));

        report.Supported.Should().BeTrue();
        report.TableExists.Should().BeTrue();
        report.IsClean.Should().BeFalse();

        var drift = report.Drifts.Should().ContainSingle().Subject;
        drift.Column.Should().Be("Amount");
        drift.Kind.Should().Be(ColumnDriftKind.TypeMismatch);
        drift.Stored.Should().Be("TEXT");
        drift.Declared.Should().Be("INTEGER");
    }

    /// <summary>
    /// ⚠ The measurement that decided the whole design (Step 0(b)). <c>DECIMAL(18,0)</c> and
    /// <c>DECIMAL(18,2)</c> are the <b>same type keyword</b>, so every provider-independent reader API
    /// answers identically for both — <c>GetDataTypeName()</c> strips the parameters and
    /// <c>GetColumnSchema()</c> reports <c>ColumnSize = -1</c> with null precision and scale.
    ///
    /// <para>
    /// A drift check built on those, which is the obvious implementation and the one a reasonable person
    /// reaches for, would report TASK-264's silent money truncation as a clean bill of health. This test
    /// is what stops someone "simplifying" the catalogue query away later.
    /// </para>
    /// </summary>
    [Fact]
    public void A_difference_only_in_SCALE_is_drift_and_is_reported()
    {
        var settings = NewDatabase();
        CreateTableWithRawDdl(settings,
            @"CREATE TABLE ""Money"" (Amount NUMERIC(18,0))");

        var connector = Connector(settings);
        var stored = connector.DetectDrift(typeof(MoneyRow));

        var drift = stored.Drifts.Should().ContainSingle(d => d.Column == "Amount").Subject;
        drift.Kind.Should().Be(ColumnDriftKind.TypeMismatch);
        drift.Stored.Should().Be("NUMERIC(18,0)");
        drift.Declared.Should().Be("NUMERIC(18,2)");
    }

    [Fact]
    public void A_column_the_model_declares_and_the_table_lacks_is_reported_as_Missing()
    {
        var settings = NewDatabase();
        CreateTableWithRawDdl(settings, @"CREATE TABLE ""Widget"" (Guid TEXT, Name TEXT)");

        var report = Connector(settings).DetectDrift(typeof(Widget));

        var drift = report.Drifts.Should().ContainSingle().Subject;
        drift.Column.Should().Be("Amount");
        drift.Kind.Should().Be(ColumnDriftKind.Missing);
        drift.Stored.Should().BeNull();
        drift.Declared.Should().Be("INTEGER");
    }

    [Fact]
    public void A_column_the_table_has_and_the_model_does_not_is_reported_as_Unexpected()
    {
        var settings = NewDatabase();
        CreateTableWithRawDdl(settings,
            @"CREATE TABLE ""Widget"" (Guid TEXT, Name TEXT, Amount INTEGER, Legacy TEXT)");

        var report = Connector(settings).DetectDrift(typeof(Widget));

        var drift = report.Drifts.Should().ContainSingle().Subject;
        drift.Column.Should().Be("Legacy");
        drift.Kind.Should().Be(ColumnDriftKind.Unexpected);
        drift.Declared.Should().BeNull();
    }

    /// <summary>
    /// A table that does not exist yet is <b>not</b> drift — a store creates its table on first use, so
    /// an entity nobody has touched legitimately has none. But it must not read as clean either, or the
    /// report says "healthy" about a question it never answered.
    /// </summary>
    [Fact]
    public void An_absent_table_is_not_drift_and_is_not_clean_either()
    {
        var settings = NewDatabase();
        CreateTableWithRawDdl(settings, @"CREATE TABLE ""Unrelated"" (X TEXT)");

        var report = Connector(settings).DetectDrift(typeof(Widget));

        report.Supported.Should().BeTrue();
        report.TableExists.Should().BeFalse();
        report.Drifts.Should().BeEmpty();
        report.IsClean.Should().BeFalse();
        report.Reason.Should().NotBeNullOrEmpty();
    }

    /// <summary>
    /// A diagnostic must not throw on the most likely mistake a host makes. <c>DataBase.LoadTable</c>
    /// answers null for a type with no <c>[Table]</c> and no <c>ModelMap</c> registration — a DTO or a
    /// view model passed by accident — and before this guard that surfaced as a
    /// <c>NullReferenceException</c> from inside the drift check.
    /// </summary>
    [Fact]
    public void An_unmapped_type_is_reported_as_unsupported_rather_than_throwing()
    {
        var settings = NewDatabase();
        var report = Connector(settings).DetectDrift(typeof(NotAnEntity));

        report.Supported.Should().BeFalse();
        report.IsClean.Should().BeFalse();
        report.Reason.Should().Contain("not a mapped entity");
    }

    [Fact]
    public void DetectDrift_refuses_a_null_type()
    {
        var settings = NewDatabase();
        Action act = () => Connector(settings).DetectDrift(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ---- the subscriber (criterion 3) -------------------------------------------------------

    [Fact]
    public async Task The_health_check_reports_Healthy_when_the_schema_matches()
    {
        var settings = NewDatabase();
        var connector = Connector(settings);
        connector.CreateTable(new[] { typeof(Widget) });

        var check = new SchemaDriftHealthCheck(() => connector, new[] { typeof(Widget) });
        var result = await check.CheckAsync();

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data!["drifted"].Should().Be(0);
    }

    /// <summary>
    /// <b>Degraded, never Unhealthy.</b> Drift is a condition for a human to act on, not a reason to
    /// pull the instance out of a load balancer — reporting it Unhealthy is how a diagnostic becomes an
    /// outage. If someone changes that, this is the test that says why it was chosen.
    /// </summary>
    [Fact]
    public async Task The_health_check_reports_Degraded_and_names_the_column_when_it_drifts()
    {
        var settings = NewDatabase();
        CreateTableWithRawDdl(settings,
            @"CREATE TABLE ""Widget"" (Guid TEXT, Name TEXT, Amount TEXT)");

        var check = new SchemaDriftHealthCheck(() => Connector(settings), new[] { typeof(Widget) });
        var result = await check.CheckAsync();

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Data!["drifted"].Should().Be(1);

        var lines = result.Data["drift"].Should().BeAssignableTo<IEnumerable<string>>().Subject.ToList();
        lines.Should().ContainSingle()
             .Which.Should().Contain("Amount").And.Contain("TEXT").And.Contain("INTEGER");
    }

    /// <summary>
    /// The report is visible where an operator looks: the description carries the counts and
    /// <c>Data</c> carries one line per finding, ready to log.
    /// </summary>
    [Fact]
    public async Task The_health_check_puts_the_finding_in_its_description_not_only_in_Data()
    {
        var settings = NewDatabase();
        CreateTableWithRawDdl(settings,
            @"CREATE TABLE ""Widget"" (Guid TEXT, Name TEXT, Amount TEXT)");

        var check = new SchemaDriftHealthCheck(() => Connector(settings), new[] { typeof(Widget) });
        var result = await check.CheckAsync();

        result.Description.Should().NotBeNullOrEmpty();
        result.Description!.Should().Contain("1 column(s) drifted");
    }

    /// <summary>
    /// ⚠ Found by the TASK-269 human review harness, not by these tests, and that is the lesson worth
    /// keeping. Every earlier assertion about an unchecked type read <c>SchemaDriftReport.IsClean</c> —
    /// which was correct — and none read the one line an operator actually sees. The check reported
    /// <c>Healthy</c> with the description <i>"Schema matches the models (1 type(s) checked)"</i> about a
    /// type whose table it had never read.
    ///
    /// <para>
    /// <b>Healthy is right and the wording was not.</b> Stores create their table on first use, so at
    /// boot every table is absent; reporting Degraded there would make every fresh deployment Degraded
    /// until each entity happened to be touched — a report an operator learns to ignore, which is its own
    /// documented defect. An unsupported provider is the opposite: permanent, so it stays Degraded. The
    /// two must not be unified.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_health_check_never_claims_a_match_for_a_table_it_did_not_check()
    {
        var settings = NewDatabase();
        CreateTableWithRawDdl(settings, @"CREATE TABLE ""Unrelated"" (X TEXT)");

        var check = new SchemaDriftHealthCheck(() => Connector(settings), new[] { typeof(Widget) });
        var result = await check.CheckAsync();

        // Healthy, because an absent table is expected and self-healing.
        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data!["tablesNotYetCreated"].Should().Be(1);

        // But the description must not assert a match it never verified.
        result.Description.Should().NotBeNull();
        result.Description!.Should().Contain("not created yet");
        result.Description.Should().Contain("0 of 1");
        result.Description.Should().NotBe("Schema matches the models (1 type(s) checked).");
    }

    /// <summary>
    /// The other half of the pair: with nothing absent, the plain wording is used. Without this, the fix
    /// above could be satisfied by always emitting the hedged sentence, which would be a different lie.
    /// </summary>
    [Fact]
    public async Task The_health_check_uses_the_plain_wording_when_everything_was_checked()
    {
        var settings = NewDatabase();
        var connector = Connector(settings);
        connector.CreateTable(new[] { typeof(Widget) });

        var result = await new SchemaDriftHealthCheck(() => connector, new[] { typeof(Widget) }).CheckAsync();

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description!.Should().Contain("1 type(s) checked");
        result.Description.Should().NotContain("not created yet");
    }

    [Fact]
    public void The_health_check_refuses_a_null_connector_factory()
    {
        Action act = () => new SchemaDriftHealthCheck(null!, new[] { typeof(Widget) });
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task The_health_check_is_Unhealthy_when_its_factory_returns_null()
    {
        var check = new SchemaDriftHealthCheck(() => null!, new[] { typeof(Widget) });
        var result = await check.CheckAsync();
        result.Status.Should().Be(HealthStatus.Unhealthy);
    }
}

/// <summary>Declared <c>NUMERIC(18,2)</c> by the SQLite connector — see the scale test.</summary>
[Birko.Data.SQL.Attributes.Table("Money")]
public class MoneyRow
{
    [Birko.Data.SQL.Attributes.PrecisionField(18)]
    [Birko.Data.SQL.Attributes.ScaleField(2)]
    public decimal Amount { get; set; }
}

/// <summary>Deliberately carries no <c>[Table]</c> attribute -- see the unmapped-type test.</summary>
public class NotAnEntity
{
    public string? Whatever { get; set; }
}
