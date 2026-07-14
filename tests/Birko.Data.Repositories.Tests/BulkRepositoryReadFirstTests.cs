using System;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.InMemory.Stores;
using Birko.Data.Models;
using Birko.Data.Repositories;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Repositories.Tests;

/// <summary>
/// CR-L171: the bulk repositories now surface ReadFirst / ReadFirstAsync for parity with the store
/// contract (where the inherited Read(filter) returns the collection, not a single entity). These verify
/// the base classes delegate to the store's single-result accessor over a real InMemory store.
/// </summary>
public class BulkRepositoryReadFirstTests
{
    private class Entity : AbstractModel
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class SyncRepo : AbstractBulkRepository<Entity>
    {
        public SyncRepo(IBulkStore<Entity> store) : base(store) { }
    }

    private sealed class AsyncRepo : AbstractAsyncBulkRepository<Entity>
    {
        public AsyncRepo(IAsyncStore<Entity> store) : base(store) { }
    }

    private static InMemoryStore<Entity> SeededStore()
    {
        var store = new InMemoryStore<Entity>();
        store.Create(new Entity { Name = "alpha" });
        store.Create(new Entity { Name = "beta" });
        return store;
    }

    private static async Task<AsyncInMemoryStore<Entity>> SeededAsyncStore()
    {
        var store = new AsyncInMemoryStore<Entity>();
        await store.CreateAsync(new Entity { Name = "alpha" });
        await store.CreateAsync(new Entity { Name = "beta" });
        return store;
    }

    [Fact]
    public void ReadFirst_returns_a_single_matching_entity()
    {
        var repo = new SyncRepo(SeededStore());

        var match = repo.ReadFirst(e => e.Name == "beta");

        match.Should().NotBeNull();
        match!.Name.Should().Be("beta");
    }

    [Fact]
    public void ReadFirst_returns_null_when_no_match()
    {
        var repo = new SyncRepo(SeededStore());

        repo.ReadFirst(e => e.Name == "missing").Should().BeNull();
    }

    [Fact]
    public async Task ReadFirstAsync_returns_a_single_matching_entity()
    {
        var repo = new AsyncRepo(await SeededAsyncStore());

        var match = await repo.ReadFirstAsync(e => e.Name == "alpha");

        match.Should().NotBeNull();
        match!.Name.Should().Be("alpha");
    }

    [Fact]
    public async Task ReadFirstAsync_returns_null_when_no_match()
    {
        var repo = new AsyncRepo(await SeededAsyncStore());

        (await repo.ReadFirstAsync(e => e.Name == "missing")).Should().BeNull();
    }
}
