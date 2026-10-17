using System;
using System.Threading.Tasks;
using Birko.Data.InMemory.Stores;
using Birko.Data.Models;
using Birko.Data.Repositories;
using Birko.Data.Stores;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Birko.Data.Repositories.Tests;

/// <summary>TASK-646, the gaps TASK-162's own fixes left in the repository contract.</summary>
public class Task646RepositoryGapsTests
{
    public class Entity : AbstractModel { public int A { get; set; } }

    public sealed class AsyncRepo : AbstractAsyncRepository<Entity>
    {
        public AsyncRepo(IAsyncStore<Entity>? store) : base(store) { }
    }

    public sealed class AsyncBulkRepo : AbstractAsyncBulkRepository<Entity>
    {
        public AsyncBulkRepo(IAsyncStore<Entity>? store) : base(store) { }
    }

    public sealed class Repo : AbstractBulkRepository<Entity>
    {
        public Repo(IBulkStore<Entity> store) : base(store) { }
    }

    /// <summary>An update or delete with no store returned as if it had worked; every other operation refuses.</summary>
    [Fact]
    public async Task An_async_repository_without_a_store_refuses_an_update_and_a_delete()
    {
        foreach (var repo in new IAsyncRepository<Entity>[] { new AsyncRepo(null), new AsyncBulkRepo(null) })
        {
            var entity = new Entity { Guid = Guid.NewGuid() };
            await repo.Invoking(r => r.UpdateAsync(entity)).Should().ThrowAsync<InvalidOperationException>();
            await repo.Invoking(r => r.DeleteAsync(entity)).Should().ThrowAsync<InvalidOperationException>();
        }
    }

    /// <summary>The default lifetime was Scoped, which this overload has refused since TASK-162, so a call without one always threw.</summary>
    [Fact]
    public void AddRepository_over_one_store_instance_works_with_its_default_lifetime()
    {
        var store = new InMemoryStore<Entity>();
        var services = new ServiceCollection().AddRepository<Repo>(store);

        services.Should().ContainSingle(d => d.ServiceType == typeof(Repo)).Which.Lifetime.Should().Be(ServiceLifetime.Singleton);
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<Repo>().Should().BeSameAs(provider.GetRequiredService<Repo>());
    }
}
