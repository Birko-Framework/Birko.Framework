using System;
using System.Linq;
using Birko.Data.InMemory.Stores;
using Birko.Data.Models;
using Birko.Data.Repositories;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.ViewModel.Tests;

/// <summary>
/// CR-H110: bulk Create/Update in AbstractBulkViewModelRepository invoked the ProcessDataDelegate
/// transform for side effects only and discarded its return value, unlike the single-item path — so
/// a delegate that returns a different/wrapped instance was silently ignored. These tests use a
/// delegate that returns a NEW model and assert the transformed instance is what gets persisted.
/// </summary>
public class BulkViewModelRepositoryDelegateTests
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

    private sealed class Repo : AbstractBulkViewModelRepository<Vm, Model>
    {
        public Repo(IBulkStore<Model> store) : base(store) { }
        protected override void MapToModel(Vm source, Model target) => target.Name = source.Name;
    }

    [Fact]
    public void BulkCreate_UsesDelegateReturnValue()
    {
        var store = new InMemoryStore<Model>();
        var repo = new Repo(store);

        // Delegate returns a NEW model instance (not the mapped one).
        repo.Create(new[] { new Vm { Name = "original" } },
            m => new Model { Guid = m.Guid, Name = "transformed" });

        var stored = ((IBulkStore<Model>)store).Read(_ => true).ToList();
        stored.Should().ContainSingle();
        stored[0].Name.Should().Be("transformed", "the delegate's returned instance must be persisted");
    }

    [Fact]
    public void BulkUpdate_UsesDelegateReturnValue()
    {
        var store = new InMemoryStore<Model>();
        var repo = new Repo(store);
        repo.Create(new[] { new Vm { Name = "seed" } });
        var existing = ((IBulkStore<Model>)store).Read(_ => true).Single();

        repo.Update(new[] { new Vm { Name = "original" } },
            m => new Model { Guid = existing.Guid, Name = "transformed" });

        var stored = ((IBulkStore<Model>)store).Read(_ => true).ToList();
        stored.Should().ContainSingle();
        stored[0].Name.Should().Be("transformed");
    }

    [Fact]
    public void BulkCreate_NoDelegate_PersistsMappedModel()
    {
        var store = new InMemoryStore<Model>();
        var repo = new Repo(store);

        repo.Create(new[] { new Vm { Name = "plain" } });

        ((IBulkStore<Model>)store).Read(_ => true).Single().Name.Should().Be("plain");
    }
}
