using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Health;
using Birko.Health.Data.SQL;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Health.Data.SQL.Tests;

/// <summary>
/// TASK-269's human review, made repeatable: renders what an <b>operator</b> sees for the three cases
/// that matter, side by side, and asserts the parts a person would be reading for.
///
/// <para>
/// <b>To read the transcript:</b>
/// <code>
/// dotnet test --nologo --filter SchemaDriftOperatorViewTests --logger "console;verbosity=detailed"
/// </code>
/// </para>
///
/// <para>
/// ⚠ <b>Why this exists as well as the per-case tests.</b> The task's human test plan asked a person to
/// read a real health report, on the grounds that *"a green automated assertion that the API returns a
/// list is not sufficient"*. That step earned its keep immediately: it found that an absent table
/// reported <c>Healthy</c> with the description <i>"Schema matches the models (1 type(s) checked)"</i> —
/// a match asserted for a type whose table had never been read. **Fifteen automated tests had missed it**,
/// because every one of them asserted <c>SchemaDriftReport.IsClean</c> — which was correct — and none
/// read the rendered status. A report model can be right while the output lies.
/// </para>
///
/// <para>
/// So this class is deliberately shaped around the <i>rendered</i> output rather than the report model,
/// and it keeps all three cases in one place so the next reviewer can compare them without standing up a
/// host. It asserts too, because a test that only prints cannot fail — and this repo has already been
/// bitten by assertions that could not fail.
/// </para>
/// </summary>
public class SchemaDriftOperatorViewTests : IDisposable
{
    private readonly string _root;
    private readonly ITestOutputHelper _output;
    private static int _seq;

    public SchemaDriftOperatorViewTests(ITestOutputHelper output)
    {
        _output = output;
        _root = Path.Combine(Path.GetTempPath(), $"birko-drift-view-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    /// <summary>The model as it stands today. The drifted database below predates two of its columns.</summary>
    [Table("Invoice")]
    public class Invoice : AbstractModel
    {
        public string? Number { get; set; }

        [PrecisionField(18)]
        [ScaleField(2)]
        public decimal Total { get; set; }

        /// <summary>Added to the model after the database was created.</summary>
        public string? Reference { get; set; }
    }

    private SqLiteSettings NewDatabase() =>
        new SqLiteSettings(_root, $"view{Interlocked.Increment(ref _seq)}.db") { CommandTimeout = 5 };

    private static void Exec(SqLiteSettings settings, string sql)
    {
        using var conn = new SqliteConnection(settings.GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>Prints a result the way a host's health endpoint or startup log would.</summary>
    private void Render(string title, HealthCheckResult r)
    {
        _output.WriteLine("");
        _output.WriteLine(new string('=', 78));
        _output.WriteLine(title);
        _output.WriteLine(new string('=', 78));
        _output.WriteLine($"  Status      : {r.Status}");
        _output.WriteLine($"  Description : {r.Description}");

        if (r.Data == null)
        {
            _output.WriteLine("  Data        : (none)");
            return;
        }

        _output.WriteLine("  Data        :");
        foreach (var kv in r.Data)
        {
            if (kv.Value is IEnumerable<string> lines)
            {
                _output.WriteLine($"     {kv.Key}:");
                foreach (var line in lines) _output.WriteLine($"        - {line}");
            }
            else
            {
                _output.WriteLine($"     {kv.Key}: {kv.Value}");
            }
        }
    }

    private async Task<HealthCheckResult> CheckAsync(SqLiteSettings settings, AbstractConnector? connector = null)
        => await new SchemaDriftHealthCheck(
               () => connector ?? new SqLiteConnector(settings),
               new[] { typeof(Invoice) }).CheckAsync();

    /// <summary>
    /// All three cases in one transcript, in the order a reviewer wants them: the control, the defect the
    /// task exists to surface, and the question that was never asked.
    /// </summary>
    [Fact]
    public async Task The_operator_view_of_all_three_cases()
    {
        // ---- 1. the control: a database this framework created -----------------------------------
        var healthy = NewDatabase();
        var connector = new SqLiteConnector(healthy);
        connector.CreateTable(new[] { typeof(Invoice) });

        var okResult = await CheckAsync(healthy, connector);
        Render("1. A database this framework created — the control", okResult);

        okResult.Status.Should().Be(HealthStatus.Healthy);
        okResult.Description!.Should().Contain("1 type(s) checked");

        // ---- 2. the case the task exists for -------------------------------------------------------
        // Written by hand, because a framework-created table cannot produce a column the framework
        // would never emit:
        //   Total     -> model declares NUMERIC(18,2); the column is still REAL   (TASK-264's shape --
        //                money as binary floating point, and the case a keyword-only check calls healthy)
        //   Reference -> model declares it; the column was never added            (a new property)
        //   Note, Legacy_Id -> present, declared by nothing                       (removed properties)
        var drifted = NewDatabase();
        Exec(drifted, @"CREATE TABLE ""Invoice"" (
                            Guid      TEXT,
                            Number    TEXT,
                            Total     REAL,
                            Note      TEXT,
                            Legacy_Id INTEGER
                        )");

        var driftResult = await CheckAsync(drifted);
        Render("2. A database whose columns were changed by hand — what an operator sees", driftResult);

        driftResult.Status.Should().Be(HealthStatus.Degraded);
        driftResult.Data!["drifted"].Should().Be(4);

        var lines = driftResult.Data["drift"].Should().BeAssignableTo<IEnumerable<string>>().Subject.ToList();
        lines.Should().HaveCount(4);
        lines.Should().ContainSingle(l => l.Contains("Invoice.Total") && l.Contains("NUMERIC(18,2)") && l.Contains("REAL"),
            "the money case is the one a keyword-only check reports as healthy, so it must be legible here");
        lines.Should().ContainSingle(l => l.Contains("Invoice.Reference") && l.Contains("not present"));
        lines.Should().ContainSingle(l => l.Contains("Invoice.Note") && l.Contains("not declared"));
        lines.Should().ContainSingle(l => l.Contains("Invoice.Legacy_Id") && l.Contains("not declared"));

        // ---- 3. the question that was never asked --------------------------------------------------
        var absent = NewDatabase();
        Exec(absent, @"CREATE TABLE ""Something_Else"" (X TEXT)");

        var absentResult = await CheckAsync(absent);
        Render("3. A table that does not exist yet — Healthy, but it must not claim a match", absentResult);

        // Healthy is deliberate: stores create their table on first use, so at boot every table is
        // absent, and Degraded here would make every fresh deployment Degraded until each entity
        // happened to be touched. The honesty requirement is on the WORDING -- which is the defect this
        // whole class was written after.
        absentResult.Status.Should().Be(HealthStatus.Healthy);
        absentResult.Data!["tablesNotYetCreated"].Should().Be(1);
        absentResult.Description!.Should().Contain("not created yet");
        absentResult.Description.Should().NotBe("Schema matches the models (1 type(s) checked).");
    }
}
