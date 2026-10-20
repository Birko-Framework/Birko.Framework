using System;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.RavenDB.Stores;
using Birko.Data.RavenDB.Tests.Stores;
using Birko.Data.RavenDB.UnitOfWork;
using FluentAssertions;
using Raven.Client.Documents.Session;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.RavenDB.Tests.UnitOfWork;

/// <summary>
/// TASK-702, live: an update inside a cluster-wide session (<see cref="RavenDbUnitOfWork"/>) of a document a cluster
/// transaction wrote. The stores evicted the loaded document and stored the caller's object, which lost the atomic guard
/// index, and the commit failed with <c>ClusterTransactionConcurrencyException</c>. Gated on <c>BIRKO_RAVEN_URL</c>.
/// </summary>
public sealed class Task702ClusterUpdateLiveTests : IDisposable
{
    private readonly RavenLive _live;

    public Task702ClusterUpdateLiveTests(ITestOutputHelper output) => _live = new RavenLive(output);

    public sealed class GuardedDoc : AbstractModel
    {
        public string? Name { get; set; }
        public int Amount { get; set; }
        public string? Note;
        public string? Stamp { get; private set; }
        public void StampWith(string stamp) => Stamp = stamp;
    }

    private AsyncRavenDBStore<GuardedDoc> NewStore() => _live.Track((url, db) => new AsyncRavenDBStore<GuardedDoc>(url, db));

    private static async Task InUnitAsync(AsyncRavenDBStore<GuardedDoc> store, Func<Task> work)
    {
        await using var unit = RavenDbUnitOfWork.FromStore(store);
        await unit.BeginAsync();
        store.SetTransactionContext(unit.Context);
        try
        {
            await work();
        }
        finally
        {
            store.SetTransactionContext(null);
        }
        await unit.CommitAsync();
    }

    [Fact]
    public async Task A_single_update_in_a_unit_of_work_commits_on_a_document_a_unit_of_work_created()
    {
        if (!_live.Ready()) return;
        var store = NewStore();
        var doc = new GuardedDoc { Guid = Guid.NewGuid(), Name = "v1", Amount = 1 };
        await InUnitAsync(store, () => store.CreateAsync(doc));

        await InUnitAsync(store, () => store.UpdateAsync(new GuardedDoc { Guid = doc.Guid, Name = "v2", Amount = 2 }));

        var stored = await store.ReadAsync(doc.Guid!.Value);
        stored!.Name.Should().Be("v2");
        stored.Amount.Should().Be(2);
    }

    [Fact]
    public async Task A_collection_update_in_a_unit_of_work_commits_on_documents_a_unit_of_work_created()
    {
        if (!_live.Ready()) return;
        var store = NewStore();
        var a = new GuardedDoc { Guid = Guid.NewGuid(), Name = "a1" };
        var b = new GuardedDoc { Guid = Guid.NewGuid(), Name = "b1" };
        await InUnitAsync(store, () => store.CreateAsync(new[] { a, b }));

        await InUnitAsync(store, () => store.UpdateAsync(new[]
        {
            new GuardedDoc { Guid = a.Guid, Name = "a2" }, new GuardedDoc { Guid = b.Guid, Name = "b2" },
        }));

        (await store.ReadAsync(a.Guid!.Value))!.Name.Should().Be("a2");
        (await store.ReadAsync(b.Guid!.Value))!.Name.Should().Be("b2");
    }

    [Fact]
    public async Task An_update_outside_a_unit_of_work_still_works_on_such_a_document_and_a_later_unit_of_work_too()
    {
        if (!_live.Ready()) return;
        var store = NewStore();
        var doc = new GuardedDoc { Guid = Guid.NewGuid(), Name = "v1" };
        await InUnitAsync(store, () => store.CreateAsync(doc));

        await store.UpdateAsync(new GuardedDoc { Guid = doc.Guid, Name = "plain" });
        (await store.ReadAsync(doc.Guid!.Value))!.Name.Should().Be("plain");

        await InUnitAsync(store, () => store.UpdateAsync(new GuardedDoc { Guid = doc.Guid, Name = "cluster again" }));
        (await store.ReadAsync(doc.Guid!.Value))!.Name.Should().Be("cluster again");
    }

    [Fact]
    public async Task Every_stored_member_is_copied_and_an_entity_read_in_the_session_updates_in_place()
    {
        if (!_live.Ready()) return;
        var store = NewStore();
        var doc = new GuardedDoc { Guid = Guid.NewGuid(), Name = "v1", Note = "n1" };
        doc.StampWith("s1");
        await InUnitAsync(store, () => store.CreateAsync(doc));

        var changed = new GuardedDoc { Guid = doc.Guid, Name = "v2", Note = "n2" };
        changed.StampWith("s2");
        await InUnitAsync(store, () => store.UpdateAsync(changed));
        var stored = await store.ReadAsync(doc.Guid!.Value);
        stored!.Note.Should().Be("n2", "a public field is stored, so it is copied");
        stored.Stamp.Should().Be("s2", "a property with a private setter is stored, so it is copied");

        await InUnitAsync(store, async () =>
        {
            var tracked = await store.ReadAsync(doc.Guid!.Value);
            tracked!.Name = "edited in place";
            await store.UpdateAsync(tracked);
        });
        (await store.ReadAsync(doc.Guid!.Value))!.Name.Should().Be("edited in place");
    }

    [Fact]
    public void The_sync_store_updates_in_a_cluster_wide_session_too()
    {
        if (!_live.Ready()) return;
        var store = _live.Track((url, db) => new RavenDBStore<GuardedDoc>(url, db));
        store.Init();
        var a = new GuardedDoc { Guid = Guid.NewGuid(), Name = "a1" };
        var b = new GuardedDoc { Guid = Guid.NewGuid(), Name = "b1" };
        void InClusterSession(Action work)
        {
            using var session = store.DocumentStore!.OpenSession(new SessionOptions { TransactionMode = TransactionMode.ClusterWide });
            store.SetTransactionContext(session);
            try { work(); } finally { store.SetTransactionContext(null); }
            session.SaveChanges();
        }
        InClusterSession(() => store.Create(new[] { a, b }));

        InClusterSession(() => store.Update(new GuardedDoc { Guid = a.Guid, Name = "a2" }));
        InClusterSession(() => store.Update(new[] { new GuardedDoc { Guid = a.Guid, Name = "a3" }, new GuardedDoc { Guid = b.Guid, Name = "b3" } }));

        store.Read(a.Guid!.Value)!.Name.Should().Be("a3");
        store.Read(b.Guid!.Value)!.Name.Should().Be("b3");
    }

    public sealed class DerivedDoc : AbstractModel
    {
        public string? Extra { get; set; }
    }

    [Fact]
    public void An_entity_of_another_runtime_type_than_the_loaded_document_is_refused_not_half_copied()
    {
        var act = () => RavenTrackedCopy.Into<AbstractModel>(new DerivedDoc { Extra = "x" }, new GuardedDoc());

        act.Should().Throw<InvalidOperationException>().WithMessage("*GuardedDoc*DerivedDoc*");
    }

    public void Dispose() => _live.Dispose();
}
