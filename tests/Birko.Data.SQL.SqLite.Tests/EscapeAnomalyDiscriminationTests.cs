using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite.Stores;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// TASK-293 — <b>what makes an escape "the anomaly", and the two ways the current answer can be wrong.</b>
///
/// <para>`AbstractConnector.CreatedTablesNamedIn` decides it by asking whether any table this connector
/// has recorded a <c>CREATE TABLE</c> for appears as a <b>substring</b> of the failing statement. Its own
/// comment justifies that with <i>"a false positive costs one extra line in an exception nobody sees
/// unless something already went wrong"</i>. TASK-288 made that false: the same answer now drives
/// <c>SchemaGeneration</c>, and therefore every store's <c>CanTrustRememberedInitialization</c>.</para>
///
/// <para>So a false positive is no longer cosmetic. It (a) reports an anomaly that did not happen on a
/// channel a host escalates, and (b) invalidates the remembered initialization of <b>every</b> store on
/// that connector, each of which then re-runs <c>CREATE TABLE IF NOT EXISTS</c> under the connector's
/// DDL lock while its own reads wait.</para>
/// </summary>
public class EscapeAnomalyDiscriminationTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _root;
    private static int _seq;

    public EscapeAnomalyDiscriminationTests(ITestOutputHelper output)
    {
        _out = output;
        _root = Path.Combine(Path.GetTempPath(), $"birko-anom-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    /// <remarks>
    /// No <c>SqliteConnection.ClearAllPools()</c> — see <c>PerStoreDoorResidueTests</c> and [[TASK-276]].
    /// </remarks>
    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    // The name pair that matters: one is a proper substring of the other, which is the relation three of
    // the eight tables in the consumer's storm evidence stand in (Movements/StockMovements,
    // Reservations/StockReservations, Events/AlarmEvents).
    [Table("Movement")]
    public class Movement : AbstractDatabaseModel
    {
        public string? Value { get; set; }
    }

    [Table("StockMovements")]
    public class StockMovements : AbstractDatabaseModel
    {
        public string? Value { get; set; }
    }

    /// <summary>A table with no relation to either, for the multi-table case.</summary>
    [Table("Ledger")]
    public class Ledger : AbstractDatabaseModel
    {
        public string? Value { get; set; }
    }

    private SqLiteSettings Fresh() => new SqLiteSettings(_root, $"anom{Interlocked.Increment(ref _seq)}.db");

    private static SqLiteConnector Connector(SqLiteSettings settings)
        => (SqLiteConnector)DataBase.GetConnector<SqLiteConnector>(settings);

    private void Report(SqLiteConnector connector, string label)
    {
        _out.WriteLine($"[{label}] escapes={connector.SchemaEscapes.Count} "
            + $"generation={connector.SchemaGeneration} "
            + $"created=[{string.Join(", ", connector.TablesCreated.Keys.OrderBy(x => x))}]");
        foreach (var escape in connector.SchemaEscapes)
        {
            _out.WriteLine($"  ESCAPE [{string.Join(", ", escape.TableNames)}] {escape.Annotation}");
        }
    }

    // ── channel (a): a created table whose name is a SUBSTRING of the failing one ────────────────────

    /// <summary>
    /// An ordinary lazy first touch of <c>StockMovements</c> — a table this connector has never created —
    /// must read as the benign case. The connector has created <c>Movement</c>, whose name is a substring
    /// of the statement.
    /// </summary>
    /// <remarks>
    /// The count goes through the connector directly rather than through a store, which is how
    /// <c>VanishedTableHealingTests</c> already models "ordinary lazy first-touch": a store would create
    /// the table before counting it, and the condition under test is precisely a count that reaches the
    /// database before its table exists.
    /// </remarks>
    [Fact]
    public async Task AFirstTouchOfALongerNamedTable_IsNotTheAnomaly()
    {
        var settings = Fresh();
        var connector = Connector(settings);

        // Give the connector a recorded create for the SHORTER name.
        var movements = new AsyncSQLiteStore<Movement>();
        movements.SetSettings(settings);
        await movements.CreateAsync(new Movement { Guid = Guid.NewGuid(), Value = "seed" });
        connector.TablesCreated.Keys.Should().Contain("Movement");

        var count = connector.SelectCount(typeof(StockMovements));
        Report(connector, "substring");

        count.Should().Be(0, "TASK-285: a count of a table that does not exist is 0");
        connector.SchemaEscapes.Should().BeEmpty(
            "StockMovements was never created, so this is the benign first touch that happens ~245 times "
            + "per consumer bring-up — not the anomaly. 'Movement' merely occurs inside the statement "
            + "text, which says nothing about whether the FAILING table was ever created");
        connector.SchemaGeneration.Should().Be(0,
            "and a false anomaly is not merely a wrong log line: it invalidates the remembered "
            + "initialization of every store on this connector (TASK-288)");
    }

    // ── channel (b): a multi-table statement, with no name collision at all ─────────────────────────

    /// <summary>
    /// The same false positive with no substring trickery: a statement naming <b>two</b> tables, one
    /// created and one not. The failing table is the uncreated one; the created one is legitimately
    /// referenced.
    /// </summary>
    /// <remarks>
    /// This is the shape a view or a multi-type count produces, and it needs no unlucky naming — which is
    /// why it matters more than channel (a). A fix that only tightens the substring match to whole
    /// identifiers closes (a) and leaves this open.
    /// </remarks>
    [Fact]
    public async Task AMultiTableStatementWhoseMissingTableWasNeverCreated_IsNotTheAnomaly()
    {
        var settings = Fresh();
        var connector = Connector(settings);

        var ledgers = new AsyncSQLiteStore<Ledger>();
        ledgers.SetSettings(settings);
        await ledgers.CreateAsync(new Ledger { Guid = Guid.NewGuid(), Value = "seed" });
        connector.TablesCreated.Keys.Should().Contain("Ledger");
        connector.TablesCreated.Keys.Should().NotContain("StockMovements");

        var count = connector.SelectCount(new[] { typeof(Ledger), typeof(StockMovements) }, (System.Linq.Expressions.LambdaExpression?)null);
        Report(connector, "multi-table");

        connector.SchemaEscapes.Should().BeEmpty(
            "the table the database reported missing is StockMovements, which this connector never "
            + "created. Ledger's presence in the statement is not evidence about StockMovements");
        connector.SchemaGeneration.Should().Be(0);
    }

    // ── the true positive, which must keep working ───────────────────────────────────────────────────

    /// <summary>
    /// The control. Narrowing the discriminator must not lose the case the channel exists for: a table
    /// this connector created, reported missing.
    /// </summary>
    [Fact]
    public async Task ATableThisConnectorCreatedAndThatVanished_IS_STILL_TheAnomaly()
    {
        var settings = Fresh();
        var connector = Connector(settings);

        var movements = new AsyncSQLiteStore<Movement>();
        movements.SetSettings(settings);
        await movements.CreateAsync(new Movement { Guid = Guid.NewGuid(), Value = "seed" });

        connector.DropTable(new[] { typeof(Movement) });
        var count = connector.SelectCount(typeof(Movement));
        Report(connector, "true-positive");

        count.Should().Be(0);
        connector.SchemaEscapes.Should().ContainSingle(
            "this is the whole point of the channel and no narrowing may lose it")
            .Which.TableNames.Should().Contain("Movement");
        connector.SchemaGeneration.Should().Be(1, "TASK-288's heal depends on this bump");
    }

    /// <summary>
    /// And the true positive in the multi-table shape, so the fix is not "give up on statements naming
    /// more than one table".
    /// </summary>
    [Fact]
    public async Task AVanishedTableInAMultiTableStatement_IS_STILL_TheAnomaly()
    {
        var settings = Fresh();
        var connector = Connector(settings);

        var ledgers = new AsyncSQLiteStore<Ledger>();
        ledgers.SetSettings(settings);
        await ledgers.CreateAsync(new Ledger { Guid = Guid.NewGuid(), Value = "seed" });
        var movements = new AsyncSQLiteStore<Movement>();
        movements.SetSettings(settings);
        await movements.CreateAsync(new Movement { Guid = Guid.NewGuid(), Value = "seed" });

        connector.DropTable(new[] { typeof(Movement) });
        connector.SelectCount(new[] { typeof(Ledger), typeof(Movement) }, (System.Linq.Expressions.LambdaExpression?)null);
        Report(connector, "true-positive-multi");

        connector.SchemaEscapes.Should().ContainSingle()
            .Which.TableNames.Should().Contain("Movement");
    }

    // ── what the provider's own error says, which is the candidate discriminator ─────────────────────

    /// <summary>
    /// Step 0 for the fix: does SQLite's error name the missing table? If it does, the honest
    /// discriminator is "was <i>that</i> table created", which closes both channels above at once.
    /// </summary>
    [Fact]
    public void TheProviderErrorNamesTheMissingTable()
    {
        var settings = Fresh();
        using var db = new SqliteConnection(settings.GetConnectionString());
        db.Open();
        using var seed = db.CreateCommand();
        seed.CommandText = "CREATE TABLE Ledger (Id INTEGER)";
        seed.ExecuteNonQuery();

        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT count(*) as count FROM \"Ledger\" AS Ledger, "
            + "\"StockMovements\" AS StockMovements";
        var act = () => cmd.ExecuteScalar();

        var thrown = act.Should().Throw<SqliteException>().Which;
        _out.WriteLine($"code={thrown.SqliteErrorCode} message={thrown.Message}");

        thrown.Message.Should().Contain("StockMovements",
            "if the error names the missing table then the discriminator can be exact rather than a "
            + "substring search over the statement");
        thrown.Message.Should().NotContain("Ledger",
            "and it must NOT name the tables that were fine, or extracting from it buys nothing");
    }
}
