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
/// CR-L237: <see cref="AbstractAsyncBulkViewModelRepository{TViewModel, TModel}"/>'s bulk
/// Create/Update/Delete(IEnumerable) built <c>data.Select(LoadModelInstance).Where(m => m != null).ToList()!</c>.
/// LoadModelInstance always returns a non-null TModel (CreateModelInstance → Store.CreateInstance() or
/// Activator.CreateInstance&lt;TModel&gt;()), so the <c>.Where(m => m != null)</c> was dead and the
/// <c>.ToList()!</c> a needless snapshot; the projection is now passed lazily. These tests prove no
/// items are dropped by the change — the full set flows through to the store on all three bulk paths.
/// </summary>
public class AsyncBulkViewModelRepositoryProjectionTests
{
    public class Model : AbstractModel
    {
        public string? Name { get; set; }
    }

    public class Vm : ILoadable<Model>
    {
        public Guid? Guid { get; set; }
        public string? Name { get; set; }
        public void LoadFrom(Model data) { Guid = data.Guid; Name = data.Name; }
    }

    private sealed class Repo : AbstractAsyncBulkViewModelRepository<Vm, Model>
    {
        public Repo(IAsyncStore<Model> store) : base(store) { }
        protected override void MapToModel(Vm source, Model target)
        {
            target.Guid = source.Guid;
            target.Name = source.Name;
        }
    }

    private static async Task<List<Model>> AllAsync(AsyncInMemoryStore<Model> store)
    {
        var result = new List<Model>();
        foreach (var m in await store.ReadAsync(null, null, null, null))
        {
            result.Add(m);
        }
        return result;
    }

    [Fact]
    public async Task BulkCreate_PersistsEveryItem_NotJustNonNull()
    {
        var store = new AsyncInMemoryStore<Model>();
        var repo = new Repo(store);

        await repo.CreateAsync(Enumerable.Range(0, 5).Select(i => new Vm { Name = $"item-{i}" }));

        var stored = await AllAsync(store);
        stored.Should().HaveCount(5);
        stored.Select(m => m.Name).Should().BeEquivalentTo("item-0", "item-1", "item-2", "item-3", "item-4");
    }

    [Fact]
    public async Task BulkUpdate_AppliesToEveryItem()
    {
        var store = new AsyncInMemoryStore<Model>();
        var repo = new Repo(store);
        await repo.CreateAsync(Enumerable.Range(0, 3).Select(i => new Vm { Name = $"before-{i}" }));

        // Re-read as view models (Guid now populated), rename each, push back through bulk UpdateAsync.
        var vms = (await AllAsync(store)).Select(m => new Vm { Guid = m.Guid, Name = m.Name + "-after" }).ToList();
        await repo.UpdateAsync(vms);

        var stored = await AllAsync(store);
        stored.Should().HaveCount(3);
        stored.Select(m => m.Name).Should().OnlyContain(n => n!.EndsWith("-after"));
    }

    [Fact]
    public async Task BulkDelete_RemovesEveryItem()
    {
        var store = new AsyncInMemoryStore<Model>();
        var repo = new Repo(store);
        await repo.CreateAsync(Enumerable.Range(0, 4).Select(i => new Vm { Name = $"d-{i}" }));

        var vms = (await AllAsync(store)).Select(m => new Vm { Guid = m.Guid, Name = m.Name }).ToList();
        vms.Should().HaveCount(4);
        await repo.DeleteAsync(vms);

        (await AllAsync(store)).Should().BeEmpty();
    }
}
