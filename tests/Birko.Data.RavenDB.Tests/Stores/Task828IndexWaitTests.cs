using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.RavenDB.Stores;
using Birko.Data.RavenDB.UnitOfWork;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.RavenDB.Tests.Stores;

/// <summary>
/// TASK-828 (the user's decision): every write a RavenDB store makes waits for the indexes before returning, so a query
/// that follows sees it. Measured before: under suite load an update applied to all 6 documents (read back by id) while a
/// query on the value it wrote returned 5. Staleness itself only shows under load, so this asserts the mechanism, which
/// is deterministic. The behavioural check is the computed-set contract, which no longer waits for the indexes itself.
/// <para>
/// TASK-999 (the user's decision) moved the wait out of the write: the server-side wait inside the save
/// (<c>waitForIndexesTimeout</c> on <c>/bulk_docs</c>) was sometimes cancelled by the server after it had committed the
/// batch, and the client reported the applied write as failed. Now each write batch is sent without a wait and is
/// followed by a separate wait (it starts with the index statistics), whose failure never fails the write.
/// </para>
/// </summary>
public sealed class Task828IndexWaitTests : IDisposable
{
    public class Doc : AbstractModel
    {
        public string? Name { get; set; }
        public int Hits { get; set; }
    }

    private readonly RavenLive _live;

    public Task828IndexWaitTests(ITestOutputHelper output) => _live = new RavenLive(output);

    public void Dispose() => _live.Dispose();

    /// <summary>
    /// A document store for <paramref name="db"/> that records the requests it sends, in order, and fails the index
    /// statistics request while <paramref name="failIndexStats"/> says so. The hook can only be attached before the store
    /// is initialized, so the test builds it and hands it to the store.
    /// </summary>
    private static Raven.Client.Documents.IDocumentStore Recording(string url, string db, ConcurrentQueue<string> requests, Func<bool>? failIndexStats = null)
    {
        var documentStore = new Raven.Client.Documents.DocumentStore { Urls = new[] { url }, Database = db };
        documentStore.OnBeforeRequest += (_, e) =>
        {
            requests.Enqueue(e.Url);
            if (failIndexStats?.Invoke() == true && IsIndexStats(e.Url))
                throw new InvalidOperationException("index statistics refused by the test");
        };
        documentStore.Initialize();
        try { documentStore.Maintenance.Server.Send(new Raven.Client.ServerWide.Operations.CreateDatabaseOperation(new Raven.Client.ServerWide.DatabaseRecord(db))); }
        catch (Raven.Client.Exceptions.ConcurrencyException) { }
        return documentStore;
    }

    private static bool IsBatch(string url) => url.Contains("/bulk_docs", StringComparison.OrdinalIgnoreCase);
    private static bool IsIndexStats(string url) => url.Contains("/indexes/stats", StringComparison.OrdinalIgnoreCase);

    // Every write batch carries no server-side wait and is followed, before the next batch, by the separate wait.
    private static void EveryBatchIsFollowedByTheWait(IReadOnlyList<string> requests, int atLeast)
    {
        var batches = requests.Select((u, i) => (Url: u, At: i)).Where(x => IsBatch(x.Url)).ToList();
        batches.Should().HaveCountGreaterThanOrEqualTo(atLeast);
        batches.Should().OnlyContain(b => !b.Url.Contains("waitForIndexesTimeout="), "the wait is no longer inside the write (TASK-999)");
        foreach (var batch in batches)
        {
            var next = requests.Skip(batch.At + 1).TakeWhile(u => !IsBatch(u));
            next.Should().Contain(u => IsIndexStats(u), "a write returns only once it has waited for the indexes (TASK-828)");
        }
    }

    [Fact]
    public void Every_sync_write_waits_for_the_indexes()
    {
        if (!_live.Ready()) return;
        var requests = new ConcurrentQueue<string>();
        var store = _live.Track((url, db) => new RavenDBStore<Doc>(Recording(url, db, requests)));

        var guid = store.Create(new Doc { Name = "a" });
        store.Create(new[] { new Doc { Name = "b" }, new Doc { Name = "c" } });
        var doc = store.Read(guid)!;
        doc.Name = "a2";
        store.Update(doc);
        store.Update(x => x.Name == "b", new PropertyUpdate<Doc>().Increment(x => x.Hits, 1));
        store.Delete(doc);

        EveryBatchIsFollowedByTheWait(requests.ToList(), 5);
    }

    [Fact]
    public async Task Every_async_write_waits_for_the_indexes()
    {
        if (!_live.Ready()) return;
        var requests = new ConcurrentQueue<string>();
        var store = _live.Track((url, db) => new AsyncRavenDBStore<Doc>(Recording(url, db, requests)));

        var guid = await store.CreateAsync(new Doc { Name = "a" });
        await store.CreateAsync(new[] { new Doc { Name = "b" }, new Doc { Name = "c" } });
        var doc = (await store.ReadAsync(guid))!;
        doc.Name = "a2";
        await store.UpdateAsync(doc);
        await store.UpdateAsync(x => x.Name == "b", new PropertyUpdate<Doc>().Increment(x => x.Hits, 1));
        await store.DeleteAsync(doc);

        EveryBatchIsFollowedByTheWait(requests.ToList(), 5);
    }

    [Fact]
    public async Task A_unit_of_work_commit_waits_for_the_indexes()
    {
        if (!_live.Ready()) return;
        var requests = new ConcurrentQueue<string>();
        var store = _live.Track((url, db) => new AsyncRavenDBStore<Doc>(Recording(url, db, requests)));

        await using (var uow = RavenDbUnitOfWork.FromStore(store))
        {
            await uow.BeginAsync();
            store.SetTransactionContext(uow.Context);
            await store.CreateAsync(new Doc { Name = "in-uow" });
            await uow.CommitAsync();
            store.SetTransactionContext(null);
        }

        EveryBatchIsFollowedByTheWait(requests.ToList(), 1);
        (await store.ReadAsync(x => x.Name == "in-uow", null, null, null)).Should().ContainSingle();
    }

    [Fact]
    public async Task A_write_whose_index_wait_fails_is_reported_as_applied()
    {
        if (!_live.Ready()) return;
        var requests = new ConcurrentQueue<string>();
        var fail = false;
        var store = _live.Track((url, db) => new AsyncRavenDBStore<Doc>(Recording(url, db, requests, () => fail)));
        await store.InitAsync();

        fail = true;
        var guid = await store.CreateAsync(new Doc { Name = "applied" });
        var created = (await store.ReadAsync(guid))!;
        await store.DeleteAsync(new[] { created });
        var kept = await store.CreateAsync(new Doc { Name = "kept" });
        fail = false;

        requests.Should().Contain(u => IsIndexStats(u), "the wait was attempted and refused");
        (await store.ReadAsync(guid)).Should().BeNull("the delete was applied, and reported as such");
        (await store.ReadAsync(kept))!.Name.Should().Be("kept");
    }

    [Fact]
    public void A_sync_write_whose_index_wait_fails_is_reported_as_applied()
    {
        if (!_live.Ready()) return;
        var requests = new ConcurrentQueue<string>();
        var fail = false;
        var store = _live.Track((url, db) => new RavenDBStore<Doc>(Recording(url, db, requests, () => fail)));
        store.Init();

        fail = true;
        var guid = store.Create(new Doc { Name = "applied" });
        fail = false;

        requests.Should().Contain(u => IsIndexStats(u));
        store.Read(guid)!.Name.Should().Be("applied");
    }
}
