using System;
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
/// ViewModel analogue of CR-H080 (surfaced by the CR-L234 review): the async bulk ViewModel
/// repository's DestroyAsync override called base.DestroyAsync (→ Store.DestroyAsync) and then
/// BulkStore.DestroyAsync — but BulkStore is <c>Store as IAsyncBulkStore</c>, the SAME instance,
/// so one store was destroyed twice (unsafe for a non-idempotent DestroyAsync: double-dispose /
/// double-close). The override was removed; these tests pin single destruction.
/// </summary>
public class BulkViewModelRepositoryDestroyTests
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

    private sealed class CountingStore : AsyncInMemoryStore<Model>
    {
        public int DestroyCalls { get; private set; }

        public override Task DestroyAsync(CancellationToken ct = default)
        {
            DestroyCalls++;
            return base.DestroyAsync(ct);
        }
    }

    private sealed class AsyncRepo : AbstractAsyncBulkViewModelRepository<Vm, Model>
    {
        public AsyncRepo(IAsyncStore<Model> store) : base(store) { }
        protected override void MapToModel(Vm source, Model target) => target.Name = source.Name;
    }

    [Fact]
    public async Task DestroyAsync_DestroysTheStoreExactlyOnce()
    {
        var store = new CountingStore();
        var repo = new AsyncRepo(store);

        await repo.DestroyAsync();

        store.DestroyCalls.Should().Be(1,
            "BulkStore is the same instance as Store — the removed override destroyed it twice");
    }

    [Fact]
    public void DestroyAsync_IsNotRedeclared_OnTheBulkViewModelRepository()
    {
        typeof(AbstractAsyncBulkViewModelRepository<Vm, Model>)
            .GetMethod("DestroyAsync")!.DeclaringType
            .Should().NotBe(typeof(AbstractAsyncBulkViewModelRepository<Vm, Model>),
                "the double-destroy override was removed (ViewModel analogue of CR-H080)");
    }
}
