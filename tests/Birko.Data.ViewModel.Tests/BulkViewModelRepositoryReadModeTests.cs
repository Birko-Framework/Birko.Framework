using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.InMemory.Stores;
using Birko.Data.Models;
using Birko.Data.Repositories;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.ViewModel.Tests;

/// <summary>
/// CR-M180: the bulk write methods on the ViewModel bulk repositories (sync + async) lacked the
/// <c>if (ReadMode) throw</c> guard the single-item paths enforce, so a repository placed in Read Mode
/// still mutated the store through its bulk overloads. Every bulk Create/Update/Delete must reject.
/// CR-M179: the async bulk ReadAsync inlined CreateInstance()+LoadFrom (skipping StoreHash), so
/// change-tracking was never primed for bulk-read entities — it now routes through LoadInstance.
/// </summary>
public class BulkViewModelRepositoryReadModeTests
{
    public class Model : AbstractModel
    {
        public string? Name { get; set; }
    }

    public class Vm : ILoadable<Model>
    {
        public string? Name { get; set; }
        public void LoadFrom(Model data) => Name = data.Name;
    }

    private sealed class SyncRepo : AbstractBulkViewModelRepository<Vm, Model>
    {
        public SyncRepo(IBulkStore<Model> store) : base(store) { }
        protected override void MapToModel(Vm source, Model target) => target.Name = source.Name;
    }

    private sealed class AsyncRepo : AbstractAsyncBulkViewModelRepository<Vm, Model>
    {
        public readonly List<Guid> Hashed = new();
        public AsyncRepo(IAsyncStore<Model> store) : base(store) { }
        protected override void MapToModel(Vm source, Model target) => target.Name = source.Name;
        protected override void StoreHash(Model data)
        {
            if (data?.Guid is Guid g) Hashed.Add(g);
            base.StoreHash(data!);
        }
    }

    // ---- CR-M180: sync bulk guards ----

    [Fact]
    public void ReadMode_blocks_sync_bulk_create()
    {
        var repo = new SyncRepo(new InMemoryStore<Model>()) { ReadMode = true };
        Action act = () => repo.Create(new[] { new Vm { Name = "x" } });
        act.Should().Throw<AccessViolationException>();
    }

    [Fact]
    public void ReadMode_blocks_sync_bulk_update_and_filter_overloads()
    {
        var repo = new SyncRepo(new InMemoryStore<Model>()) { ReadMode = true };
        ((Action)(() => repo.Update(new[] { new Vm() }))).Should().Throw<AccessViolationException>();
        ((Action)(() => repo.Update(_ => true, m => m.Name = "y"))).Should().Throw<AccessViolationException>();
        ((Action)(() => repo.Update(_ => true, new PropertyUpdate<Model>()))).Should().Throw<AccessViolationException>();
    }

    [Fact]
    public void ReadMode_blocks_sync_bulk_delete_and_filter_overload()
    {
        var repo = new SyncRepo(new InMemoryStore<Model>()) { ReadMode = true };
        ((Action)(() => repo.Delete(new[] { new Vm() }))).Should().Throw<AccessViolationException>();
        ((Action)(() => repo.Delete(_ => true))).Should().Throw<AccessViolationException>();
    }

    [Fact]
    public void Sync_bulk_create_still_works_when_not_in_read_mode()
    {
        var store = new InMemoryStore<Model>();
        var repo = new SyncRepo(store);
        repo.Create(new[] { new Vm { Name = "ok" } });
        ((IBulkStore<Model>)store).Read(_ => true).Single().Name.Should().Be("ok");
    }

    // ---- CR-M180: async bulk guards ----

    [Fact]
    public async Task ReadMode_blocks_async_bulk_writes()
    {
        var repo = new AsyncRepo(new AsyncInMemoryStore<Model>()) { ReadMode = true };
        await FluentActions.Awaiting(() => repo.CreateAsync(new[] { new Vm() })).Should().ThrowAsync<AccessViolationException>();
        await FluentActions.Awaiting(() => repo.UpdateAsync(new[] { new Vm() })).Should().ThrowAsync<AccessViolationException>();
        await FluentActions.Awaiting(() => repo.UpdateAsync(_ => true, m => m.Name = "y")).Should().ThrowAsync<AccessViolationException>();
        await FluentActions.Awaiting(() => repo.UpdateAsync(_ => true, new PropertyUpdate<Model>())).Should().ThrowAsync<AccessViolationException>();
        await FluentActions.Awaiting(() => repo.DeleteAsync(_ => true)).Should().ThrowAsync<AccessViolationException>();
        await FluentActions.Awaiting(() => repo.DeleteAsync(new[] { new Vm() })).Should().ThrowAsync<AccessViolationException>();
    }

    // ---- CR-M179: async bulk ReadAsync primes change-tracking ----

    [Fact]
    public async Task Async_bulk_ReadAsync_stores_hash_for_each_entity()
    {
        var store = new AsyncInMemoryStore<Model>();
        await store.CreateAsync(new Model { Name = "a" });
        await store.CreateAsync(new Model { Name = "b" });

        var repo = new AsyncRepo(store);
        var loaded = new List<Vm>();
        await foreach (var vm in repo.ReadAsync())
        {
            loaded.Add(vm);
        }

        loaded.Should().HaveCount(2);
        // LoadInstance (via ReadAsync) must call StoreHash for both read models — the old inlined
        // CreateInstance()+LoadFrom path never did, so change-tracking stayed empty (CR-M179).
        repo.Hashed.Should().HaveCount(2);
    }
}
