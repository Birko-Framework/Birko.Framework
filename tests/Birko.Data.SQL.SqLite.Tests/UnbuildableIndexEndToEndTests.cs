using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite;
using Birko.Data.SQL.SqLite.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// An index that cannot be built must not take the entity's whole read surface with it.
///
/// Schema-ensure runs LAZILY on first data access (AbstractAsyncStore.EnsureInitializedAsync ->
/// InitCoreAsync -> Connector.CreateTable), and `_initialized = true` is set only AFTER InitCoreAsync
/// returns — so an exception from CREATE INDEX left the store permanently uninitialised and every
/// later operation, reads included, re-attempted and re-threw. Measured in consumer Symbio: one
/// duplicate (TenantGuid, OrderNumber) pair left over from pre-allocator numbering made a
/// later-declared UNIQUE index unbuildable and returned 500 on every route of six entities.
///
/// The failure is now RECORDED (IndexCreationFailures / OnIndexCreationFailed), not thrown.
/// </summary>
public class UnbuildableIndexEndToEndTests : IDisposable
{
    private readonly string _root;

    public UnbuildableIndexEndToEndTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"birko-badidx-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    /// <summary>The table as it exists in a deployment that predates the index declaration.</summary>
    [Table("BadIdxDocs")]
    public class LegacyDoc : AbstractLogModel
    {
        public Guid TenantGuid { get; set; }
        public string Number { get; set; } = null!;
    }

    /// <summary>
    /// The same table after a UNIQUE index is declared on it. Identical columns, so
    /// CREATE TABLE IF NOT EXISTS is a no-op and only the indexes are new. The second index is
    /// perfectly buildable — it must still be created even though the first one fails.
    /// </summary>
    [Table("BadIdxDocs")]
    public class IndexedDoc : AbstractLogModel
    {
        [IndexedField("aa_bad_unique", 0, IsUnique: true)]
        public Guid TenantGuid { get; set; }

        [IndexedField("aa_bad_unique", 1, IsUnique: true)]
        [IndexedField("zz_good_plain", 0)]
        public string Number { get; set; } = null!;
    }

    private static SqLiteSettings Settings(string root) => new SqLiteSettings(root, "badidx.db");

    /// <summary>
    /// Creates the table with NO indexes and seeds the duplicate pair that breaks the unique index.
    /// Returns the process-wide cached connector the stores in each test will share.
    /// </summary>
    private async Task<SqLiteConnector> SeedDuplicatesAsync()
    {
        var factory = new SqLiteStoreFactory(new SqLiteStoreFactoryOptions { Location = _root, Name = "badidx.db" });
        var connector = (SqLiteConnector)factory.GetConnector();
        connector.CreateTable(new[] { typeof(LegacyDoc) });

        var store = new AsyncSQLiteStore<LegacyDoc>();
        store.SetSettings(Settings(_root));

        var tenant = Guid.NewGuid();
        await store.CreateAsync(new LegacyDoc { TenantGuid = tenant, Number = "MO2026000001" });
        await store.CreateAsync(new LegacyDoc { TenantGuid = tenant, Number = "MO2026000001" }); // the duplicate
        (await store.CountAsync()).Should().Be(2, "the duplicate exists before the index is ever declared");
        return connector;
    }

    /// <summary>A fresh store instance, standing in for the scoped store a web request would resolve.</summary>
    private AsyncSQLiteStore<IndexedDoc> PerRequestStore()
    {
        var store = new AsyncSQLiteStore<IndexedDoc>();
        store.SetSettings(Settings(_root));
        return store;
    }

    private static List<string> IndexNames(SqLiteConnector connector, string table)
    {
        using var conn = connector.CreateConnection(connector.Settings);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = '{table}' AND name NOT LIKE 'sqlite_%'";
        var names = new List<string>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) names.Add(reader.GetString(0));
        return names;
    }

    [Fact]
    public async Task UnbuildableUniqueIndex_LeavesTheReadSurfaceWorking()
    {
        await SeedDuplicatesAsync();

        var store = new AsyncSQLiteStore<IndexedDoc>();
        store.SetSettings(Settings(_root));

        // Pre-fix this threw a UNIQUE-constraint error out of schema-ensure and never initialised.
        // Named argument disambiguates the zero-arg / filter overloads (TASK-138); a null filter is read-all.
        var rows = (await store.ReadAsync(filter: null)).ToList();

        rows.Should().HaveCount(2, "the rows stay reachable — an absent index must not hide the data");
    }

    [Fact]
    public async Task UnbuildableUniqueIndex_IsRecordedWithTableAndIndexName()
    {
        await SeedDuplicatesAsync();

        var store = new AsyncSQLiteStore<IndexedDoc>();
        store.SetSettings(Settings(_root));
        await store.InitAsync();

        store.Connector.Should().NotBeNull();
        var failures = store.Connector!.IndexCreationFailures;

        failures.Should().ContainSingle("exactly the one unbuildable index is reported");
        failures[0].TableName.Should().Be("BadIdxDocs");
        failures[0].IndexName.Should().Be("aa_bad_unique", "the report has to name the index a human must repair");
        failures[0].Error.Should().NotBeNull();
        failures[0].ToString().Should().Contain("aa_bad_unique").And.Contain("BadIdxDocs");
    }

    [Fact]
    public async Task UnbuildableUniqueIndex_RaisesTheEvent()
    {
        await SeedDuplicatesAsync();

        var store = new AsyncSQLiteStore<IndexedDoc>();
        store.SetSettings(Settings(_root));

        var raised = new List<IndexCreationFailure>();
        store.Connector!.OnIndexCreationFailed += f => raised.Add(f);

        await store.InitAsync();

        raised.Should().ContainSingle("a host subscribing at startup must hear about it");
        raised[0].IndexName.Should().Be("aa_bad_unique");
    }

    [Fact]
    public async Task AFailingIndex_DoesNotHideTheIndexesBehindIt()
    {
        await SeedDuplicatesAsync();

        var store = new AsyncSQLiteStore<IndexedDoc>();
        store.SetSettings(Settings(_root));
        await store.InitAsync();

        var names = IndexNames(store.Connector!, "BadIdxDocs");

        // One index per attempt: the unbuildable one is skipped, the buildable one still lands.
        // A single try/catch around the whole batch would lose this one.
        names.Should().Contain("zz_good_plain", "a buildable index declared after a failing one must still be created");
        names.Should().NotContain("aa_bad_unique", "it could not be built — that is the reported condition");
    }

    [Fact]
    public async Task TheStoreStillInitialisesAndKeepsWorkingAfterTheFailure()
    {
        await SeedDuplicatesAsync();

        var store = new AsyncSQLiteStore<IndexedDoc>();
        store.SetSettings(Settings(_root));
        await store.InitAsync();

        // Writes work too — the missing UNIQUE index is not enforced, which is the documented degradation.
        var tenant = Guid.NewGuid();
        await store.CreateAsync(new IndexedDoc { TenantGuid = tenant, Number = "MO2026000002" });

        (await store.CountAsync()).Should().Be(3);

        // And the failure is recorded once, not re-appended on every subsequent operation.
        store.Connector!.IndexCreationFailures.Should().ContainSingle();
    }

    [Fact]
    public async Task PerRequestStores_DoNotAccumulateDuplicateReports()
    {
        var connector = await SeedDuplicatesAsync();

        // Connectors are cached process-wide per (type, settings) while `_initialized` lives on the store,
        // so a scoped store per HTTP request re-runs schema-ensure against ONE shared connector. An
        // append-only list grew by one entry per request, forever.
        for (int i = 0; i < 5; i++)
        {
            await PerRequestStore().InitAsync();
        }

        connector.IndexCreationFailures.Should().ContainSingle(
            "the report is current state per (table, index), not a log of attempts");
        connector.IndexCreationFailures[0].IndexName.Should().Be("aa_bad_unique");
    }

    [Fact]
    public async Task PerRequestStores_RaiseTheEventOnceNotPerRequest()
    {
        var connector = await SeedDuplicatesAsync();

        var raised = new List<IndexCreationFailure>();
        connector.OnIndexCreationFailed += f => raised.Add(f);

        for (int i = 0; i < 5; i++)
        {
            await PerRequestStore().InitAsync();
        }

        raised.Should().ContainSingle("the event marks the transition into failure, not every attempt");
    }

    [Fact]
    public async Task RepairingTheDataClearsTheReportOnTheNextSchemaEnsure()
    {
        var connector = await SeedDuplicatesAsync();

        await PerRequestStore().InitAsync();
        connector.IndexCreationFailures.Should().ContainSingle("the duplicate is still there");

        // An operator repairs the offending row — reachable precisely because the read surface survived.
        var repairStore = PerRequestStore();
        var rows = (await repairStore.ReadAsync(filter: null)).ToList();
        await repairStore.DeleteAsync(rows.First());

        // A later store instance re-runs schema-ensure, the index now builds, and the stale report goes.
        await PerRequestStore().InitAsync();

        connector.IndexCreationFailures.Should().BeEmpty(
            "a repaired condition must not keep being reported — and the index self-heals with no restart");
        IndexNames(connector, "BadIdxDocs").Should().Contain("aa_bad_unique", "it is buildable now");
    }

    [Fact]
    public async Task TheAsyncSchemaEnsurePathDegradesTheSameWay()
    {
        var connector = await SeedDuplicatesAsync();

        // Every store/repository in the framework calls the SYNC CreateTable even from async init, so this
        // async overload is reachable only by a direct caller. It must not throw where the sync path does not.
        await connector.Invoking(c => c.CreateTableAsync(new[] { typeof(IndexedDoc) }))
                       .Should().NotThrowAsync();

        connector.IndexCreationFailures.Should().ContainSingle();
        connector.IndexCreationFailures[0].IndexName.Should().Be("aa_bad_unique");
        IndexNames(connector, "BadIdxDocs").Should().Contain("zz_good_plain",
            "the async path is also one attempt per index");
    }

    // ───────────────────── TASK-283: a throwing subscriber must not brick the entity ─────────────────

    /// <summary>
    /// <b>TASK-283 — a subscriber that throws must not defeat TASK-204's degrade.</b>
    ///
    /// <para><c>RecordIndexCreationFailure</c> raises the event <b>inside</b> the <c>catch</c> that
    /// implements the degrade, and a store sets <c>_initialized</c> only <i>after</i> schema-ensure
    /// returns. So an escaping handler exception left the entity's whole surface — <b>reads included</b> —
    /// throwing on every later operation: the failure TASK-204 exists to remove, reintroduced through the
    /// channel that reports it.</para>
    ///
    /// <para>The trigger is ordinary rather than theoretical: the event's own summary invites a host to
    /// <i>"subscribe to log or escalate"</i>, and escalating by rethrowing is a normal thing to write.</para>
    /// </summary>
    [Fact]
    public async Task AThrowingSubscriber_DoesNotDefeatTheDegrade()
    {
        var connector = await SeedDuplicatesAsync();
        connector.OnIndexCreationFailed += _ => throw new InvalidOperationException("handler escalated");

        var store = PerRequestStore();

        // The read surface is what TASK-204 bought and what a throwing subscriber took away again.
        var count = await store.CountAsync();
        count.Should().Be(2,
            "before TASK-283 the handler's exception propagated out of schema-ensure, so the store never "
            + "initialised and every later operation on this entity threw — reads included");

        connector.IndexCreationFailures.Should().ContainSingle(
            "the degrade itself is unchanged: the index that cannot be built is still recorded")
            .Which.IndexName.Should().Be("aa_bad_unique");
        IndexNames(connector, "BadIdxDocs").Should().Contain("zz_good_plain",
            "and the buildable index beside it is still created");
    }

    /// <summary>
    /// The handler's own failure is <b>recorded</b>, not discarded — otherwise a broken subscriber and an
    /// event that never fired look identical from outside.
    /// </summary>
    /// <remarks>
    /// § TASK-289's rule, which this channel now shares: swallowed means recorded. Note the sink is a
    /// collection and deliberately <b>not</b> another event — announcing a subscriber failure through a
    /// subscriber is the same hole one level up.
    /// </remarks>
    [Fact]
    public async Task AThrowingSubscribersOwnFailure_IsRecorded()
    {
        var connector = await SeedDuplicatesAsync();
        connector.OnIndexCreationFailed += _ => throw new InvalidOperationException("handler escalated");

        await PerRequestStore().CountAsync();

        connector.SubscriberFailures.Should().ContainSingle()
            .Which.Channel.Should().Be("OnIndexCreationFailed",
                "keyed by (channel, exception type), so a logging bug and a metrics bug stay "
                + "distinguishable");
        connector.SubscriberFailures[0].Error.Should().BeOfType<InvalidOperationException>();
    }

    /// <summary>
    /// <b>Per subscriber, not one <c>try</c> around the multicast.</b> A host with a logger and a metric
    /// must not lose the metric to a bug in the logger.
    /// </summary>
    [Fact]
    public async Task OneThrowingSubscriber_DoesNotSuppressTheOthers()
    {
        var connector = await SeedDuplicatesAsync();
        var second = 0;
        var third = 0;
        connector.OnIndexCreationFailed += _ => throw new InvalidOperationException("first blew up");
        connector.OnIndexCreationFailed += _ => Interlocked.Increment(ref second);
        connector.OnIndexCreationFailed += _ => Interlocked.Increment(ref third);

        await PerRequestStore().CountAsync();

        second.Should().Be(1, "a plain Invoke stops at the first delegate that throws");
        third.Should().Be(1);
        connector.SubscriberFailures.Should().ContainSingle("only the first one failed");
    }

    /// <summary>
    /// ⚠ The contract that <b>is</b> consumed, asserted so the hardening cannot quietly change it.
    /// Measured at TASK-283 across all 16 consumer repos: <b>zero</b> subscriptions to
    /// <c>OnIndexCreationFailed</c>, but <c>IndexCreationFailures</c> has a real reader. The collection is
    /// what a consumer depends on, and it must behave exactly as before whether a subscriber throws or
    /// not.
    /// </summary>
    [Fact]
    public async Task TheRecordedCollection_IsUnaffectedByWhatSubscribersDo()
    {
        var withThrower = await SeedDuplicatesAsync();
        withThrower.OnIndexCreationFailed += _ => throw new InvalidOperationException("boom");
        await PerRequestStore().CountAsync();

        withThrower.IndexCreationFailures.Should().ContainSingle();
        withThrower.IndexCreationFailures[0].TableName.Should().Be("BadIdxDocs");
        withThrower.IndexCreationFailures[0].IndexName.Should().Be("aa_bad_unique");
        withThrower.IndexCreationFailures[0].Error.Should().NotBeNull();
    }
}
