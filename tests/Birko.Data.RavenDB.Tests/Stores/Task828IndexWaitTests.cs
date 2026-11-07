using System;
using System.Collections.Concurrent;
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
/// is deterministic: every batch the client sends carries <c>waitForIndexesTimeout</c>. The behavioural check is the
/// computed-set contract, which no longer waits for the indexes itself.
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
    /// A document store for <paramref name="db"/> that records the write batches (<c>/bulk_docs</c>) it sends. The hook
    /// can only be attached before the store is initialized, so the test builds it and hands it to the store.
    /// </summary>
    private static Raven.Client.Documents.IDocumentStore Recording(string url, string db, ConcurrentQueue<string> batches)
    {
        var documentStore = new Raven.Client.Documents.DocumentStore { Urls = new[] { url }, Database = db };
        documentStore.OnBeforeRequest += (_, e) =>
        {
            if (e.Url.Contains("/bulk_docs", StringComparison.OrdinalIgnoreCase)) batches.Enqueue(e.Url);
        };
        documentStore.Initialize();
        try { documentStore.Maintenance.Server.Send(new Raven.Client.ServerWide.Operations.CreateDatabaseOperation(new Raven.Client.ServerWide.DatabaseRecord(db))); }
        catch (Raven.Client.Exceptions.ConcurrencyException) { }
        return documentStore;
    }

    [Fact]
    public void Every_sync_write_waits_for_the_indexes()
    {
        if (!_live.Ready()) return;
        var batches = new ConcurrentQueue<string>();
        var store = _live.Track((url, db) => new RavenDBStore<Doc>(Recording(url, db, batches)));

        var guid = store.Create(new Doc { Name = "a" });
        store.Create(new[] { new Doc { Name = "b" }, new Doc { Name = "c" } });
        var doc = store.Read(guid)!;
        doc.Name = "a2";
        store.Update(doc);
        store.Update(x => x.Name == "b", new PropertyUpdate<Doc>().Increment(x => x.Hits, 1));
        store.Delete(doc);

        batches.Should().HaveCountGreaterThanOrEqualTo(5);
        batches.Should().OnlyContain(u => u.Contains("waitForIndexesTimeout="), "a write returns only once the indexes include it");
    }

    [Fact]
    public async Task Every_async_write_waits_for_the_indexes()
    {
        if (!_live.Ready()) return;
        var batches = new ConcurrentQueue<string>();
        var store = _live.Track((url, db) => new AsyncRavenDBStore<Doc>(Recording(url, db, batches)));

        var guid = await store.CreateAsync(new Doc { Name = "a" });
        await store.CreateAsync(new[] { new Doc { Name = "b" }, new Doc { Name = "c" } });
        var doc = (await store.ReadAsync(guid))!;
        doc.Name = "a2";
        await store.UpdateAsync(doc);
        await store.UpdateAsync(x => x.Name == "b", new PropertyUpdate<Doc>().Increment(x => x.Hits, 1));
        await store.DeleteAsync(doc);

        batches.Should().HaveCountGreaterThanOrEqualTo(5);
        batches.Should().OnlyContain(u => u.Contains("waitForIndexesTimeout="));
    }

    [Fact]
    public async Task A_unit_of_work_commit_waits_for_the_indexes()
    {
        if (!_live.Ready()) return;
        var batches = new ConcurrentQueue<string>();
        var store = _live.Track((url, db) => new AsyncRavenDBStore<Doc>(Recording(url, db, batches)));

        await using (var uow = RavenDbUnitOfWork.FromStore(store))
        {
            await uow.BeginAsync();
            store.SetTransactionContext(uow.Context);
            await store.CreateAsync(new Doc { Name = "in-uow" });
            await uow.CommitAsync();
            store.SetTransactionContext(null);
        }

        batches.Should().NotBeEmpty();
        batches.Should().OnlyContain(u => u.Contains("waitForIndexesTimeout="));
        (await store.ReadAsync(x => x.Name == "in-uow", null, null, null)).Should().ContainSingle();
    }
}
