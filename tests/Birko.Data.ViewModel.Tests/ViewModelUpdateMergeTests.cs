using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.InMemory.Stores;
using Birko.Data.Models;
using Birko.Data.Repositories;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.ViewModel.Tests;

/// <summary>
/// SH-H034 and SH-H035, fixed together because they are one root cause seen twice: the ViewModel
/// repositories built an update from the wrong model, and asked the STORE to take two decisions it
/// cannot take.
/// <para>
/// <b>SH-H034</b> — <c>Update</c> built its model with <c>LoadModelInstance</c>, i.e.
/// <c>CreateModelInstance()</c> + <c>MapToModel</c>: a FRESH instance carrying only what the ViewModel
/// maps. The stored row was never read, and every backend writes an update whole
/// (<c>Connector.Update</c> renders every column of <c>table.GetSelectFields()</c>; the portable stores
/// replace the stored instance outright). A ViewModel is a PARTIAL projection by construction — it
/// cannot map <c>CreatedAt</c>/<c>UpdatedAt</c>, or the <c>TenantGuid</c> a store wrapper injects — so
/// every such column was silently reset to its default on every update. Filed against the two
/// single-item repositories; the two BULK repositories use the same helper, so the defect was on
/// <b>4</b> update paths and all four are covered here.
/// </para>
/// <para>
/// <b>SH-H035</b> — <c>StoreDataDelegate&lt;T&gt;</c> is declared <c>delegate T (T data)</c>, but
/// measured across the framework there are <b>96</b> <c>storeDelegate?.Invoke(...)</c> sites and
/// <b>0</b> that consume the result. So returning <c>null!</c> to mean "skip this write" suppressed
/// nothing, and a <c>ProcessDataDelegate</c> that returned a REPLACEMENT instance was dropped on
/// single-item <c>Create</c> and <c>Update</c> — while the bulk path already honoured it (CR-H110).
/// Both decisions now happen in the repository, which is the layer holding the information.
/// </para>
/// <para>
/// ⚠ The assertions here are the <b>value read back from the store</b>, never "no exception was
/// thrown" — a silent-corruption claim that a "did not throw" assertion passes against unchanged. All
/// 17 pre-existing tests in this project passed against the unfixed code and still pass, which is why
/// these defects survived.
/// </para>
/// </summary>
public class ViewModelUpdateMergeTests
{
    // ---- fixture ----

    public class Model : AbstractModel
    {
        /// <summary>Mapped by the ViewModel.</summary>
        public string? Name { get; set; }

        /// <summary>Framework-owned: set on insert, never expressible in a presentation ViewModel.</summary>
        public DateTime? CreatedAt { get; set; }

        /// <summary>Wrapper-owned: what <c>TenantStoreWrapper</c> injects. A ViewModel cannot map it.</summary>
        public Guid? TenantGuid { get; set; }

        public Model Copy() => new Model
        {
            Guid = Guid,
            Name = Name,
            CreatedAt = CreatedAt,
            TenantGuid = TenantGuid,
        };
    }

    public class Vm : ILoadable<Model>
    {
        public Guid? Guid { get; set; }
        public string? Name { get; set; }

        /// <summary>
        /// Displayed, never written back — the ordinary shape for an audit column. No <c>MapToModel</c>
        /// below assigns it, which is exactly why <c>Update</c> could blank it in the store and then
        /// blank it here too.
        /// </summary>
        public DateTime? CreatedAt { get; set; }

        public void LoadFrom(Model data)
        {
            Guid = data.Guid;
            Name = data.Name;
            CreatedAt = data.CreatedAt;
        }
    }

    private sealed class SyncRepo : AbstractViewModelRepository<Vm, Model>
    {
        public SyncRepo(IStore<Model> store) : base(store) { }

        protected override void MapToModel(Vm source, Model target)
        {
            target.Guid = source.Guid;
            target.Name = source.Name;
        }
    }

    private sealed class AsyncRepo : AbstractAsyncViewModelRepository<Vm, Model>
    {
        public AsyncRepo(IAsyncStore<Model> store) : base(store) { }

        protected override void MapToModel(Vm source, Model target)
        {
            target.Guid = source.Guid;
            target.Name = source.Name;
        }
    }

    private sealed class SyncBulkRepo : AbstractBulkViewModelRepository<Vm, Model>
    {
        public SyncBulkRepo(IBulkStore<Model> store) : base(store) { }

        protected override void MapToModel(Vm source, Model target)
        {
            target.Guid = source.Guid;
            target.Name = source.Name;
        }
    }

    private sealed class AsyncBulkRepo : AbstractAsyncBulkViewModelRepository<Vm, Model>
    {
        public AsyncBulkRepo(IAsyncStore<Model> store) : base(store) { }

        protected override void MapToModel(Vm source, Model target)
        {
            target.Guid = source.Guid;
            target.Name = source.Name;
        }
    }

    /// <summary>
    /// Hands back DETACHED copies and counts writes.
    /// <para>
    /// The detaching is load-bearing: <c>AbstractInMemoryStore</c> returns the stored instance by
    /// reference (SH-H016's mechanism), so with a plain <c>InMemoryStore</c> the repository's merge
    /// would mutate the stored object directly and the "an unchanged update issues no write" assertion
    /// would pass whether or not the write was actually suppressed. Copying on the way out and on the
    /// way in makes the stored state genuinely independent, which is how every real backend behaves.
    /// </para>
    /// </summary>
    private sealed class ProbeStore : InMemoryStore<Model>
    {
        public int UpdateCalls { get; private set; }

        // NB: on a bulk store a 1-argument base.ReadCore(filter) binds the BULK overload (it hides the
        // single-result one from member lookup -- CLAUDE.md § Conventions), so the 4-argument form is
        // spelled out to pick a deliberate overload rather than whichever one lookup happens to reach.
        protected override Model? ReadCore(Expression<Func<Model, bool>>? filter = null)
            => base.ReadCore(filter, null, 1, null).FirstOrDefault()?.Copy();

        protected override IEnumerable<Model> ReadCore(Expression<Func<Model, bool>>? filter = null, OrderBy<Model>? orderBy = null, int? limit = null, int? offset = null)
            => base.ReadCore(filter, orderBy, limit, offset).Select(x => x.Copy()).ToList();

        protected override void UpdateCore(Model data, StoreDataDelegate<Model>? storeDelegate = null)
        {
            UpdateCalls++;
            base.UpdateCore(data.Copy(), storeDelegate);
        }

        protected override void UpdateCore(IEnumerable<Model> data, StoreDataDelegate<Model>? storeDelegate = null)
        {
            var copies = data.Select(x => x.Copy()).ToList();
            UpdateCalls += copies.Count;
            base.UpdateCore(copies, storeDelegate);
        }
    }

    /// <summary>Asynchronous twin of <see cref="ProbeStore"/>.</summary>
    private sealed class AsyncProbeStore : AsyncInMemoryStore<Model>
    {
        public int UpdateCalls { get; private set; }

        // See ProbeStore.ReadCore for why the 4-argument overload is spelled out.
        protected override async Task<Model?> ReadCoreAsync(Expression<Func<Model, bool>>? filter = null, CancellationToken ct = default)
            => (await base.ReadCoreAsync(filter, null, 1, null, ct)).FirstOrDefault()?.Copy();

        protected override async Task<IEnumerable<Model>> ReadCoreAsync(Expression<Func<Model, bool>>? filter = null, OrderBy<Model>? orderBy = null, int? limit = null, int? offset = null, CancellationToken ct = default)
            => (await base.ReadCoreAsync(filter, orderBy, limit, offset, ct)).Select(x => x.Copy()).ToList();

        protected override Task UpdateCoreAsync(Model data, StoreDataDelegate<Model>? storeDelegate = null, CancellationToken ct = default)
        {
            UpdateCalls++;
            return base.UpdateCoreAsync(data.Copy(), storeDelegate, ct);
        }

        protected override Task UpdateCoreAsync(IEnumerable<Model> data, StoreDataDelegate<Model>? storeDelegate = null, CancellationToken ct = default)
        {
            var copies = data.Select(x => x.Copy()).ToList();
            UpdateCalls += copies.Count;
            return base.UpdateCoreAsync(copies, storeDelegate, ct);
        }
    }

    private static readonly DateTime SeededCreatedAt = new DateTime(2020, 3, 4, 5, 6, 7, DateTimeKind.Utc);
    private static readonly Guid SeededTenant = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static Guid Seed(ProbeStore store, string name = "original")
        => store.Create(new Model { Name = name, CreatedAt = SeededCreatedAt, TenantGuid = SeededTenant });

    private static Task<Guid> SeedAsync(AsyncProbeStore store, string name = "original")
        => store.CreateAsync(new Model { Name = name, CreatedAt = SeededCreatedAt, TenantGuid = SeededTenant });

    // ---- SH-H034: an update must not blank what the ViewModel does not map ----

    [Fact]
    public void Sync_single_update_preserves_the_columns_the_view_model_does_not_map()
    {
        var store = new ProbeStore();
        var guid = Seed(store);
        var repo = new SyncRepo(store);

        var vm = repo.Read()!;
        vm.Name = "edited";

        repo.Update(vm);

        var stored = store.Read(guid);
        stored.Should().NotBeNull();
        stored!.Name.Should().Be("edited", "the mapped column is what the caller asked to change");
        stored.CreatedAt.Should().Be(SeededCreatedAt, "SH-H034: an unmapped framework-owned column must survive the update");
        stored.TenantGuid.Should().Be(SeededTenant, "SH-H034: a wrapper-injected column must survive the update");
    }

    [Fact]
    public async Task Async_single_update_preserves_the_columns_the_view_model_does_not_map()
    {
        var store = new AsyncProbeStore();
        var guid = await SeedAsync(store);
        var repo = new AsyncRepo(store);

        var vm = (await repo.ReadAsync())!;
        vm.Name = "edited";

        await repo.UpdateAsync(vm);

        var stored = await store.ReadAsync(guid);
        stored.Should().NotBeNull();
        stored!.Name.Should().Be("edited");
        stored.CreatedAt.Should().Be(SeededCreatedAt, "SH-H034, async twin: separate code, so it needs its own assertion");
        stored.TenantGuid.Should().Be(SeededTenant);
    }

    [Fact]
    public void Sync_bulk_update_preserves_the_columns_the_view_model_does_not_map()
    {
        var store = new ProbeStore();
        var guid = Seed(store);
        var repo = new SyncBulkRepo(store);

        var vm = new Vm { Guid = guid, Name = "edited" };

        repo.Update(new[] { vm });

        var stored = store.Read(guid);
        stored!.Name.Should().Be("edited");
        stored.CreatedAt.Should().Be(SeededCreatedAt, "SH-H034 is on 4 update paths; guarding only the two filed ones would ship a merging single-item update beside a blanking bulk one");
        stored.TenantGuid.Should().Be(SeededTenant);
    }

    [Fact]
    public async Task Async_bulk_update_preserves_the_columns_the_view_model_does_not_map()
    {
        var store = new AsyncProbeStore();
        var guid = await SeedAsync(store);
        var repo = new AsyncBulkRepo(store);

        var vm = new Vm { Guid = guid, Name = "edited" };

        await repo.UpdateAsync(new[] { vm });

        var stored = await store.ReadAsync(guid);
        stored!.Name.Should().Be("edited");
        stored.CreatedAt.Should().Be(SeededCreatedAt);
        stored.TenantGuid.Should().Be(SeededTenant);
    }

    [Fact]
    public void The_caller_view_model_is_refreshed_from_the_merged_row_not_from_the_defaults()
    {
        var store = new ProbeStore();
        Seed(store);
        var repo = new SyncRepo(store);

        var vm = repo.Read()!;
        vm.Name = "edited";
        repo.Update(vm);

        // Update ends with data.LoadFrom(item). Before the fix `item` was the partially-mapped fresh
        // instance, so the caller's own ViewModel was refreshed from those defaults too.
        vm.Name.Should().Be("edited");
        vm.CreatedAt.Should().Be(SeededCreatedAt,
            "the caller's ViewModel is refreshed from the model that was persisted, so before the fix it was refreshed from the fresh instance's defaults");
    }

    // ---- SH-H035: decisions the store cannot take are taken in the repository ----

    [Fact]
    public void An_unchanged_update_STILL_issues_its_write()
    {
        var store = new ProbeStore();
        Seed(store);
        var repo = new SyncRepo(store);

        var vm = repo.Read()!;   // records the hash
        repo.Update(vm);         // nothing changed

        store.UpdateCalls.Should().Be(1,
            "SH-H035's skip half is deliberately NOT enabled. The `return null!` suppressed nothing because no backend reads a StoreDataDelegate's return value, and making it real would silently drop the audit stamp, the UpdatedAt bump and the domain event -- AuditStoreWrapper, TimestampStoreWrapper and EventSourcingStoreWrapper all sit inside Store.Update in StoreWrapperBuilder's recommended chain. That decision is TASK-453; until it is taken, the write stays unconditional exactly as it has always behaved.");
    }

    [Fact]
    public void A_changed_update_still_issues_its_write()
    {
        var store = new ProbeStore();
        Seed(store);
        var repo = new SyncRepo(store);

        var vm = repo.Read()!;
        vm.Name = "edited";
        repo.Update(vm);

        store.UpdateCalls.Should().Be(1, "a changed update must persist; with the skip deliberately unimplemented this is a contract pin rather than a bound on a guard");
        store.Read(vm.Guid!.Value)!.Name.Should().Be("edited");
    }

    [Fact]
    public async Task An_unchanged_async_update_STILL_issues_its_write_and_a_changed_one_persists()
    {
        var store = new AsyncProbeStore();
        await SeedAsync(store);
        var repo = new AsyncRepo(store);

        var vm = (await repo.ReadAsync())!;
        await repo.UpdateAsync(vm);
        store.UpdateCalls.Should().Be(1, "see the synchronous twin -- the skip half is deliberately not enabled (TASK-453)");

        vm.Name = "edited";
        await repo.UpdateAsync(vm);
        store.UpdateCalls.Should().Be(2);
        (await store.ReadAsync(vm.Guid!.Value))!.Name.Should().Be("edited");
    }

    [Fact]
    public void A_process_delegate_returning_a_replacement_instance_is_honoured_on_create()
    {
        var store = new ProbeStore();
        var repo = new SyncRepo(store);

        repo.Create(new Vm { Name = "typed" }, _ => new Model { Name = "replacement", CreatedAt = SeededCreatedAt });

        store.Read(store.Read(null, null, 1, null).Single().Guid!.Value)!.Name.Should().Be("replacement",
            "SH-H035: ProcessDataDelegate is a transform; the pre-transform model used to be persisted because the store discards the delegate's return value");
    }

    [Fact]
    public void A_process_delegate_returning_a_replacement_instance_is_honoured_on_update()
    {
        var store = new ProbeStore();
        var guid = Seed(store);
        var repo = new SyncRepo(store);

        var vm = repo.Read()!;
        vm.Name = "edited";
        repo.Update(vm, m => new Model { Guid = m.Guid, Name = "replacement", CreatedAt = m.CreatedAt, TenantGuid = m.TenantGuid });

        store.Read(guid)!.Name.Should().Be("replacement");
    }

    [Fact]
    public async Task A_process_delegate_returning_a_replacement_instance_is_honoured_on_the_async_paths()
    {
        var store = new AsyncProbeStore();
        var repo = new AsyncRepo(store);

        await repo.CreateAsync(new Vm { Name = "typed" }, _ => new Model { Name = "replacement" });
        (await store.ReadAsync(null, null, 1, null)).Single().Name.Should().Be("replacement");
    }

    // ---- contract pins: these pass either way and are recorded as pins, not as evidence ----

    [Fact]
    public void Create_still_builds_a_FRESH_model_and_does_not_merge()
    {
        var store = new ProbeStore();
        var repo = new SyncRepo(store);

        repo.Create(new Vm { Name = "new-row" });

        var stored = store.Read(null, null, 1, null).Single();
        stored.Name.Should().Be("new-row");
        stored.CreatedAt.Should().BeNull("a create has no stored row to merge with; LoadModelInstance is correct there and is deliberately unchanged");
    }

    [Fact]
    public void Updating_a_view_model_whose_row_does_not_exist_behaves_as_before()
    {
        var store = new ProbeStore();
        var repo = new SyncRepo(store);

        var act = () => repo.Update(new Vm { Guid = Guid.NewGuid(), Name = "ghost" });

        act.Should().NotThrow("the merge must not invent a different failure when the row is missing — the store reports the miss the way it always has");
        store.Read(null, null, null, null).Should().BeEmpty();
    }

    // ---- the review pass's findings: a live-reference store, a failing write, a concurrent writer ----

    /// <summary>
    /// Returns the stored instance BY REFERENCE, exactly as every portable backend does, and can be made
    /// to fail one write.
    /// <para>
    /// ⚠ This store exists because <see cref="ProbeStore"/> cannot see the defect its own detaching
    /// hides. <c>AbstractInMemoryStore</c>, <c>AbstractJsonStore</c> and <c>AbstractXmlStore</c> all hand
    /// back the instance in their own collection, so a merge that maps onto it mutates store state
    /// <b>before</b> and <b>independently of</b> <c>Store.Update</c> — and on the file-backed stores the
    /// next unrelated write flushes that never-committed state to disk. The production merge therefore
    /// maps onto a detached copy, and only a live-reference store can prove it.
    /// </para>
    /// </summary>
    private sealed class LiveRefStore : InMemoryStore<Model>
    {
        public bool FailNextUpdate { get; set; }
        public int UpdateCalls { get; private set; }

        protected override void UpdateCore(Model data, StoreDataDelegate<Model>? storeDelegate = null)
        {
            UpdateCalls++;
            if (FailNextUpdate)
            {
                FailNextUpdate = false;
                throw new InvalidOperationException("simulated write failure");
            }
            // Store a copy so that what is committed is independent of the caller's instance; reads
            // still hand back a live reference, which is the property under test.
            base.UpdateCore(data.Copy(), storeDelegate);
        }
    }

    private sealed class AsyncLiveRefStore : AsyncInMemoryStore<Model>
    {
        public bool FailNextUpdate { get; set; }

        protected override Task UpdateCoreAsync(Model data, StoreDataDelegate<Model>? storeDelegate = null, CancellationToken ct = default)
        {
            if (FailNextUpdate)
            {
                FailNextUpdate = false;
                throw new InvalidOperationException("simulated write failure");
            }
            return base.UpdateCoreAsync(data.Copy(), storeDelegate, ct);
        }
    }

    private static Guid SeedLive(LiveRefStore store, string name = "original")
        => store.Create(new Model { Name = name, CreatedAt = SeededCreatedAt, TenantGuid = SeededTenant });

    [Fact]
    public void A_failed_update_leaves_the_stored_row_untouched()
    {
        var store = new LiveRefStore();
        var guid = SeedLive(store);
        var repo = new SyncRepo(store);

        var vm = repo.Read()!;
        vm.Name = "edited";
        store.FailNextUpdate = true;

        ((Action)(() => repo.Update(vm))).Should().Throw<InvalidOperationException>();

        store.Read(guid)!.Name.Should().Be("original",
            "the merge must map onto a DETACHED copy — mapping onto the instance the store handed back applies the update to store state before, and independently of, the write");
    }

    [Fact]
    public async Task A_failed_async_update_leaves_the_stored_row_untouched()
    {
        var store = new AsyncLiveRefStore();
        var guid = await store.CreateAsync(new Model { Name = "original", CreatedAt = SeededCreatedAt, TenantGuid = SeededTenant });
        var repo = new AsyncRepo(store);

        var vm = (await repo.ReadAsync())!;
        vm.Name = "edited";
        store.FailNextUpdate = true;

        await FluentActions.Awaiting(() => repo.UpdateAsync(vm)).Should().ThrowAsync<InvalidOperationException>();

        (await store.ReadAsync(guid))!.Name.Should().Be("original",
            "the async merge is separate code from the sync one and needs its own detach assertion -- a mutation showed the sync test alone leaves the async path unguarded");
    }

    [Fact]
    public void An_update_retried_after_a_failed_write_still_lands()
    {
        var store = new LiveRefStore();
        var guid = SeedLive(store);
        var repo = new SyncRepo(store);

        var vm = repo.Read()!;
        vm.Name = "edited";

        store.FailNextUpdate = true;
        ((Action)(() => repo.Update(vm))).Should().Throw<InvalidOperationException>();

        repo.Update(vm);   // the caller retries, as a transient-failure policy would

        store.Read(guid)!.Name.Should().Be("edited",
            "guards TASK-453 against the naive re-enable: refreshing the hash before the write records the new value as persisted, so a retry after a failed write sees 'nothing changed' and silently drops the update");
    }

    [Fact]
    public async Task An_async_update_retried_after_a_failed_write_still_lands()
    {
        var store = new AsyncLiveRefStore();
        var guid = await store.CreateAsync(new Model { Name = "original", CreatedAt = SeededCreatedAt, TenantGuid = SeededTenant });
        var repo = new AsyncRepo(store);

        var vm = (await repo.ReadAsync())!;
        vm.Name = "edited";

        store.FailNextUpdate = true;
        await FluentActions.Awaiting(() => repo.UpdateAsync(vm)).Should().ThrowAsync<InvalidOperationException>();

        await repo.UpdateAsync(vm);

        (await store.ReadAsync(guid))!.Name.Should().Be("edited");
    }

    [Fact]
    public void An_update_that_restores_a_concurrently_changed_row_is_not_skipped()
    {
        var store = new ProbeStore();
        var guid = Seed(store, "A");
        var repo = new SyncRepo(store);

        var vm = repo.Read()!;   // hash recorded for "A"

        // Another writer changes the row underneath us.
        store.Update(new Model { Guid = guid, Name = "B", CreatedAt = SeededCreatedAt, TenantGuid = SeededTenant });

        // The caller submits the value they originally read, i.e. asks to restore "A".
        repo.Update(vm);

        store.Read(guid)!.Name.Should().Be("A",
            "guards TASK-453 against the naive re-enable: gating the write on the hash recorded at the caller's earlier read matches here and silently drops this write, leaving the other writer's value");
    }

    [Fact]
    public void A_process_delegate_on_create_can_read_the_assigned_key()
    {
        var store = new ProbeStore();
        var repo = new SyncRepo(store);

        Guid? seenByDelegate = null;
        repo.Create(new Vm { Name = "x" }, m =>
        {
            seenByDelegate = m.Guid;
            return m;
        });

        seenByDelegate.Should().NotBeNull(
            "CreateCore assigns data.Guid before invoking the store delegate, so a transform that used to run there could stamp related records with the new key; hoisting it out must not take that away");
        store.Read(seenByDelegate!.Value).Should().NotBeNull("the key the delegate saw must be the key the row was stored under");
    }

    [Fact]
    public async Task A_process_delegate_on_async_create_can_read_the_assigned_key()
    {
        var store = new AsyncProbeStore();
        var repo = new AsyncRepo(store);

        Guid? seenByDelegate = null;
        await repo.CreateAsync(new Vm { Name = "x" }, m =>
        {
            seenByDelegate = m.Guid;
            return m;
        });

        seenByDelegate.Should().NotBeNull();
        (await store.ReadAsync(seenByDelegate!.Value)).Should().NotBeNull();
    }
}
