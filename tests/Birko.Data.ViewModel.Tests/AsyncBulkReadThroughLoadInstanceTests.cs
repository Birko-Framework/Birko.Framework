using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Birko.Data.InMemory.Stores;
using Birko.Data.Models;
using Birko.Data.Repositories;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.ViewModel.Tests;

/// <summary>
/// CR-M179: the async bulk <c>ReadAsync</c> inlined <c>CreateInstance()+LoadFrom</c>, so an override of the
/// <c>LoadInstance</c> hook did not apply to bulk reads. It routes through the hook. (This file held the <c>ReadMode</c>
/// guard tests until TASK-453 removed <c>ReadMode</c>; the hook test then counted hash calls, which went with the hash.)
/// </summary>
public class AsyncBulkReadThroughLoadInstanceTests
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

    private sealed class AsyncRepo : AbstractAsyncBulkViewModelRepository<Vm, Model>
    {
        public readonly List<Guid> Loaded = new();
        public AsyncRepo(IAsyncStore<Model> store) : base(store) { }
        protected override void MapToModel(Vm source, Model target) => target.Name = source.Name;
        public override Vm? LoadInstance(Model? model = null)
        {
            if (model?.Guid is Guid g) Loaded.Add(g);
            return base.LoadInstance(model);
        }
    }

    [Fact]
    public async Task Async_bulk_ReadAsync_loads_every_entity_through_the_LoadInstance_hook()
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
        repo.Loaded.Should().HaveCount(2, "the bulk read goes through the LoadInstance hook for each entity");
    }
}
