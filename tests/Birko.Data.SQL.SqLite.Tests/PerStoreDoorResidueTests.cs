using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.SQL.Stores;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// TASK-290 — <b>TASK-244's residue is closed on one of its two transaction doors.</b>
///
/// <para>TASK-244's rule is <i>"schema-ensure participates in the caller's boundary, and a participating
/// schema-ensure is not remembered"</i>, and its own acceptance asked for one answer applied identically
/// to the ambient door (<c>SqlUnitOfWork</c>) and the per-store door (<c>SetTransactionContext</c>). The
/// first half landed on both — <c>SchemaEnsureRollbackResidueTests</c> pins that the per-store door now
/// puts the DDL inside the boundary. The second half did not, and the reason is an evaluation
/// ORDER:</para>
///
/// <code>
/// // AbstractAsyncStore.EnsureInitializedAsync
/// await InitCoreAsync(ct);
/// _initialized = CanRememberInitialization;      // reads Connector.DdlSurvivesRollback
///
/// // AsyncDataBaseStore.InitCoreAsync
/// using var _tx = EnterTransactionScope();       // publishes the PER-STORE context as an ambient...
/// await Task.Run(() =&gt; Connector.CreateTable(...));
/// }                                              // ...and DISPOSES it here, before the line above runs
/// </code>
///
/// <para>So with the per-store door the ambient is gone by the time <c>CanRememberInitialization</c> is
/// asked, <c>DdlSurvivesRollback</c> answers <c>true</c> on the strength of
/// <c>AmbientTransaction == null</c>, and the store remembers an initialization that is still inside a
/// caller's uncommitted transaction. With <c>SqlUnitOfWork</c> the caller holds the ambient across the
/// whole operation, so the same expression answers <c>false</c> and the store correctly forgets.</para>
///
/// <para><b>Why this matters beyond tidiness.</b> It manufactures the exact signature of the escape
/// TASK-290 is chasing, on a legitimate path with no <c>DROP</c> anywhere: the connector has a recorded
/// <c>CREATE TABLE</c> for the table, the store's init gate has passed, and the table is not there. That
/// is what <c>EnsureSchemaAndReport</c> reports as <i>"but this connector already created it"</i>.</para>
///
/// <para>⚠ <b>And it is NOT SQLite-specific.</b> The condition is
/// <c>AmbientTransaction != null &amp;&amp; SupportsTransactionalDdl</c>, which holds on SQLite,
/// PostgreSQL and SQL Server alike; only MySQL is exempt, and only because its DDL commits itself
/// (TASK-243). The per-provider live suites carry the same assertions.</para>
/// </summary>
public class PerStoreDoorResidueTests : IDisposable
{
    private readonly string _root;
    private readonly ITestOutputHelper _output;
    private static int _seq;

    public PerStoreDoorResidueTests(ITestOutputHelper output)
    {
        _output = output;
        _root = Path.Combine(Path.GetTempPath(), $"birko-door-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    /// <remarks>
    /// ⚠ <b>No <c>SqliteConnection.ClearAllPools()</c> here, deliberately — measured.</b> Twenty-four of
    /// this project's teardowns call it, and it is <b>process-wide</b>: it reaches every other test class's
    /// pooled connections, including ones with a command in flight, because xUnit runs collections in
    /// parallel. Adding three more copies of it took this suite from 6/6 clean to a reproducible ~1-2 in 6
    /// cross-class failure (<c>TransactionBoundaryEndToEndTests</c> and this class's own
    /// <c>BeginTransaction</c>, both with SQLITE_BUSY). Removing it from the classes added here took it
    /// back to clean. Each test owns its own database file, so there is nothing for a pool clear to buy.
    /// <para>
    /// That is a data point for [[TASK-276]], whose leading hypothesis for the pre-existing rare
    /// cross-class failure in <c>Birko.Data.SQL.Tests</c> was exactly these calls, and which killed it by
    /// measuring one clear against one in-flight connection in isolation. It reproduces at suite scale
    /// rather than in isolation — do not read that task's "wrong, and now recorded as wrong" as covering
    /// this.
    /// </para>
    /// </remarks>
    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    public class DoorRow : AbstractModel
    {
        public string? Name { get; set; }
    }

    private sealed class DoorRowMapping : IModelMapping<DoorRow>
    {
        public void Configure(ModelMap<DoorRow> map)
        {
            map.ToTable("DoorRows").HasPrimary(x => x.Guid).HasUnique(x => x.Guid);
            map.Property(x => x.Name).HasPrecision(100);
        }
    }

    private SqLiteSettings NewDatabase()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new DoorRowMapping());
        registry.ApplyToDatabase();
        // The framework's default timeout, deliberately. An earlier draft set 3 s "so a lock wait fails
        // fast", and that made this suite flake when xUnit ran it beside ColdTableStormTests, whose 200
        // concurrent first touches saturate the disk: BeginTransaction failed with SQLITE_BUSY in a test
        // that is about visibility and expects no contention at all. Nothing here waits on a lock in the
        // healthy case, so there is no hang to guard against and the short timeout only bought a flake.
        return new SqLiteSettings(_root, $"door{Interlocked.Increment(ref _seq)}.db");
    }

    private static AsyncSQLiteStore<DoorRow> AsyncStore(SqLiteSettings settings)
    {
        var store = new AsyncSQLiteStore<DoorRow>();
        store.SetSettings(settings);
        return store;
    }

    private static SqLiteConnector Connector(SqLiteSettings settings)
        => (SqLiteConnector)DataBase.GetConnector<SqLiteConnector>(settings);

    private static bool TableExists(SqLiteSettings settings, string table)
    {
        using var connection = new SqliteConnection(settings.GetConnectionString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @t";
        command.Parameters.AddWithValue("@t", table);
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    // ── the measurement ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ThePerStoreDoor_DoesNotRememberAnInitThatCanStillBeRolledBack()
    {
        var settings = NewDatabase();
        var store = AsyncStore(settings);
        var connector = Connector(settings);

        using var connection = new SqliteConnection(settings.GetConnectionString());
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        store.SetTransactionContext(new SqlTransactionContext(connection, transaction));
        await store.CreateAsync(new DoorRow { Guid = Guid.NewGuid(), Name = "first attempt" });
        transaction.Rollback();
        store.SetTransactionContext(null);

        TableExists(settings, "DoorRows").Should().BeFalse(
            "TASK-244 put the per-store door's DDL inside the boundary, so the rollback removes it — this "
            + "is the precondition, already pinned by SchemaEnsureRollbackResidueTests");
        connector.TablesCreated.Keys.Should().Contain("DoorRows",
            "the create is recorded whether or not it survived, which is what makes the next failure read "
            + "as the anomaly rather than as an ordinary first touch");

        // The plainest possible next operation on the same store instance: no boundary at all.
        var guid = await store.CreateAsync(new DoorRow { Guid = Guid.NewGuid(), Name = "second attempt" });
        _output.WriteLine($"second CreateAsync returned {guid}; table exists = {TableExists(settings, "DoorRows")}");

        TableExists(settings, "DoorRows").Should().BeTrue(
            "the store must not remember an initialization the caller could still undo. TASK-244 closed "
            + "this for SqlUnitOfWork, where the caller holds the ambient across the whole operation; on "
            + "the per-store door EnterTransactionScope() is entered and disposed INSIDE InitCoreAsync, so "
            + "AbstractAsyncStore evaluates CanRememberInitialization after the ambient is already gone");

        var rows = await store.ReadAsync(x => x.Name == "second attempt", null, null, null, default);
        rows.Should().ContainSingle("the write reported success, so the row must be there");
    }

    /// <summary>
    /// The same residue seen from the escape channel — which is why this is TASK-290's business and not
    /// only TASK-244's loose end.
    /// </summary>
    [Fact]
    public async Task TheResidue_NoLongerManufacturesAnAnomalousEscape()
    {
        var settings = NewDatabase();
        var store = AsyncStore(settings);
        var connector = Connector(settings);

        using var connection = new SqliteConnection(settings.GetConnectionString());
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();
        store.SetTransactionContext(new SqlTransactionContext(connection, transaction));
        await store.CreateAsync(new DoorRow { Guid = Guid.NewGuid(), Name = "first attempt" });
        transaction.Rollback();
        store.SetTransactionContext(null);

        // A count, which is the statement shape every observed occurrence has had.
        var count = await store.CountAsync();

        _output.WriteLine($"count={count} escapes={connector.SchemaEscapes.Count} "
            + $"generation={connector.SchemaGeneration}");
        foreach (var escape in connector.SchemaEscapes)
        {
            _output.WriteLine($"  ESCAPE [{string.Join(", ", escape.TableNames)}] {escape.Annotation}");
        }

        connector.SchemaEscapes.Should().BeEmpty(
            "an anomalous escape here would mean the framework can manufacture TASK-290's signature on a "
            + "legitimate path — recorded CREATE TABLE, init gate passed, table absent — with no DROP and "
            + "no concurrency at all");
    }

    /// <summary>
    /// <b>The sync store is the worse half of the same defect, and it had no unaffected door to compare
    /// against.</b> <c>SqlUnitOfWork.FromStore</c> takes an <c>AsyncDataBaseStore</c>, so
    /// <c>SetTransactionContext</c> is the <i>only</i> transaction door a sync store has — the one that
    /// did not work. <c>AbstractStore</c> and <c>AbstractAsyncStore</c> keep their own <c>_initialized</c>
    /// and their own gate, so a fix applied to one of the two is how half of this looks green.
    /// </summary>
    [Fact]
    public void TheSyncStore_DoesNotRememberAnInitThatCanStillBeRolledBack()
    {
        var settings = NewDatabase();
        var store = new SQLiteStore<DoorRow>();
        store.SetSettings(settings);

        using var connection = new SqliteConnection(settings.GetConnectionString());
        connection.Open();
        using var transaction = connection.BeginTransaction();

        store.SetTransactionContext(new SqlTransactionContext(connection, transaction));
        store.Create(new DoorRow { Guid = Guid.NewGuid(), Name = "first attempt" });
        transaction.Rollback();
        store.SetTransactionContext(null);

        TableExists(settings, "DoorRows").Should().BeFalse();

        store.Create(new DoorRow { Guid = Guid.NewGuid(), Name = "second attempt" });
        TableExists(settings, "DoorRows").Should().BeTrue(
            "the sync store's only transaction door is the broken one, so before this fix a sync store "
            + "that ever ran a per-store boundary was left believing an undone init");
    }

    /// <summary>
    /// The control: the ambient door must keep answering correctly, so a fix to the per-store door cannot
    /// be a blanket "never remember" that costs every boundary-less store a schema-ensure per operation.
    /// </summary>
    [Fact]
    public async Task TheAmbientDoor_StillForgets_AndAStoreWithNoBoundaryStillRemembers()
    {
        var settings = NewDatabase();
        var store = AsyncStore(settings);
        var connector = Connector(settings);

        await using (var uow = SqlUnitOfWork.FromStore(store))
        {
            await uow.BeginAsync();
            await store.CreateAsync(new DoorRow { Guid = Guid.NewGuid(), Name = "first attempt" });
            await uow.RollbackAsync();
        }

        TableExists(settings, "DoorRows").Should().BeFalse();
        await store.CreateAsync(new DoorRow { Guid = Guid.NewGuid(), Name = "second attempt" });
        TableExists(settings, "DoorRows").Should().BeTrue("TASK-244's own measurement, unchanged");

        // And the steady state costs nothing: a store with no boundary anywhere remembers its init.
        var generation = connector.SchemaGeneration;
        for (var i = 0; i < 5; i++)
        {
            await store.CreateAsync(new DoorRow { Guid = Guid.NewGuid(), Name = $"n{i}" });
        }
        connector.SchemaGeneration.Should().Be(generation);
        (await store.CountAsync()).Should().Be(6);
    }
}
