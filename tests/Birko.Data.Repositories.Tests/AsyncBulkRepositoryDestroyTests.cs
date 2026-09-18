using System.Threading;
using System.Threading.Tasks;
using Birko.Data.InMemory.Stores;
using Birko.Data.Models;
using Birko.Data.Repositories;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Repositories.Tests;

/// <summary>
/// CR-H080: AbstractAsyncBulkRepository.DestroyAsync called base.DestroyAsync (which destroys Store)
/// and then BulkStore.DestroyAsync — but BulkStore is the SAME instance as Store, so the store was
/// destroyed twice (unsafe for a non-idempotent DestroyAsync). This test proves it now destroys once.
/// </summary>
public class AsyncBulkRepositoryDestroyTests
{
    private class Entity : AbstractModel { }

    private class CountingStore : AsyncInMemoryStore<Entity>
    {
        public int DestroyCount;
        public override Task DestroyAsync(CancellationToken ct = default)
        {
            DestroyCount++;
            return base.DestroyAsync(ct);
        }
    }

    private class TestRepository : AbstractAsyncBulkRepository<Entity>
    {
        public TestRepository(IAsyncStore<Entity> store) : base(store) { }
    }

    [Fact]
    public async Task DestroyAsync_DestroysUnderlyingStoreExactlyOnce()
    {
        var store = new CountingStore();
        var repo = new TestRepository(store);

        await repo.DestroyAsync();

        store.DestroyCount.Should().Be(1);
    }
}
