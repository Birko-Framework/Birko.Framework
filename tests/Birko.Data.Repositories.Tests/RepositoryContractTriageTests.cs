using System;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.InMemory.Stores;
using Birko.Data.Models;
using Birko.Data.Repositories;
using Birko.Data.Stores;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Birko.Data.Repositories.Tests;

/// <summary>TASK-162, the medium findings in <c>repository-contract</c>: the locator, DI registration, the async bulk repository.</summary>
[Collection("RepositoryLocator")] // the locator is a process-wide cache
public class RepositoryContractTriageTests
{
    public class Entity : AbstractModel { public int A { get; set; } }

    public sealed class Repo : AbstractBulkRepository<Entity>
    {
        public Repo(IBulkStore<Entity> store) : base(store) { }
    }

    public sealed class OtherRepo : AbstractBulkRepository<Entity>
    {
        public OtherRepo(IBulkStore<Entity> store) : base(store) { }
    }

    public sealed class AsyncRepo : AbstractAsyncBulkRepository<Entity>
    {
        public AsyncRepo(IAsyncStore<Entity>? store) : base(store) { }
    }

    /// <summary>SH-M210: created under the store type's name, destroyed under the repository type's, so never found.</summary>
    [Fact]
    public void The_no_key_destroy_finds_the_repository_the_no_key_get_created()
    {
        var store = new InMemoryStore<Entity>();
        var first = RepositoryLocator.GetRepository<InMemoryStore<Entity>, Repo>(store);
        RepositoryLocator.Destroy<Repo>();

        RepositoryLocator.GetRepository<InMemoryStore<Entity>, Repo>(new InMemoryStore<Entity>())
            .Should().NotBeSameAs(first, "the destroy evicted it, so a new one is built");
        RepositoryLocator.Destroy<Repo>();
    }

    /// <summary>SH-M211: a second store of the same type was silently ignored on a cache hit.</summary>
    [Fact]
    public void A_different_store_for_a_cached_repository_is_refused()
    {
        var key = Guid.NewGuid().ToString();
        RepositoryLocator.GetRepository<InMemoryStore<Entity>, Repo>(new InMemoryStore<Entity>(), key);
        var act = () => RepositoryLocator.GetRepository<InMemoryStore<Entity>, Repo>(new InMemoryStore<Entity>(), key);
        act.Should().Throw<InvalidOperationException>().WithMessage("*different store*");
        RepositoryLocator.Destroy<Repo>(key);
    }

    [Fact]
    public void The_same_store_for_a_cached_repository_returns_it()
    {
        var key = Guid.NewGuid().ToString();
        var store = new InMemoryStore<Entity>();
        var first = RepositoryLocator.GetRepository<InMemoryStore<Entity>, Repo>(store, key);
        RepositoryLocator.GetRepository<InMemoryStore<Entity>, Repo>(store, key).Should().BeSameAs(first);
        RepositoryLocator.Destroy<Repo>(key);
    }

    /// <summary>SH-M212: destroying one repository destroyed the store a sibling under the same key still pointed at.</summary>
    [Fact]
    public void Destroying_a_repository_evicts_every_repository_on_the_same_store()
    {
        var key = Guid.NewGuid().ToString();
        var store = new InMemoryStore<Entity>();
        RepositoryLocator.GetRepository<InMemoryStore<Entity>, Repo>(store, key);
        var sibling = RepositoryLocator.GetRepository<InMemoryStore<Entity>, OtherRepo>(store, key);

        RepositoryLocator.Destroy<Repo>(key);

        RepositoryLocator.GetRepository<InMemoryStore<Entity>, OtherRepo>(new InMemoryStore<Entity>(), key)
            .Should().NotBeSameAs(sibling, "the sibling pointed at the destroyed store, so it was evicted with it");
        RepositoryLocator.Destroy<OtherRepo>(key);
    }

    /// <summary>SH-M218: one store instance was shared by a Scoped or Transient registration's every resolution.</summary>
    [Theory]
    [InlineData(ServiceLifetime.Scoped)]
    [InlineData(ServiceLifetime.Transient)]
    public void A_repository_over_one_store_instance_cannot_be_scoped_or_transient(ServiceLifetime lifetime)
    {
        var act = () => new ServiceCollection().AddRepository<Repo>(new InMemoryStore<Entity>(), lifetime);
        act.Should().Throw<ArgumentException>().WithMessage("*Singleton*");
        new ServiceCollection().AddRepository<Repo>(new InMemoryStore<Entity>(), ServiceLifetime.Singleton).Should().NotBeNull();
    }

    /// <summary>SH-M209: an async bulk repository without a bulk store answered empty and did nothing; the sync one throws.</summary>
    [Fact]
    public async Task An_async_bulk_repository_without_a_bulk_store_refuses()
    {
        var repo = new AsyncRepo(null);
        await repo.Invoking(r => r.ReadAsync(x => x.A > 0, null, null, null)).Should().ThrowAsync<InvalidOperationException>();
        await repo.Invoking(r => r.DeleteAsync(x => x.A == 1)).Should().ThrowAsync<InvalidOperationException>();
    }
}
