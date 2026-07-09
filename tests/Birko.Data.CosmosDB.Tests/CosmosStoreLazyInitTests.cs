using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.CosmosDB.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.CosmosDB.Tests;

/// <summary>
/// Regressions for CR-H042 / CR-H043: the Cosmos stores overrode the public Read/ReadAsync methods
/// directly, skipping the base lazy-init gate — so a store read before an explicit Init returned
/// null instead of auto-initializing. The overrides now run EnsureInitialized(Async) first, which
/// we prove by tracking InitCore invocation.
/// </summary>
public class CosmosStoreLazyInitTests
{
    private sealed class TrackingAsyncStore : AsyncCosmosDBStore<TestModel>
    {
        public int InitCount;
        protected override Task InitCoreAsync(CancellationToken ct = default)
        {
            InitCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class TrackingStore : CosmosDBStore<TestModel>
    {
        public int InitCount;
        protected override void InitCore()
        {
            InitCount++;
        }
    }

    [Fact]
    public async Task AsyncReadByGuid_TriggersLazyInit()
    {
        var store = new TrackingAsyncStore();

        await store.ReadAsync(Guid.NewGuid());

        store.InitCount.Should().Be(1, "CR-H042: ReadAsync(Guid) must run the lazy-init gate");
    }

    [Fact]
    public void SyncReadByGuid_TriggersLazyInit()
    {
        var store = new TrackingStore();

        store.Read(Guid.NewGuid());

        store.InitCount.Should().Be(1, "CR-H043: Read(Guid) must run the lazy-init gate");
    }

    [Fact]
    public void SyncReadAll_TriggersLazyInit()
    {
        var store = new TrackingStore();

        _ = store.Read();

        store.InitCount.Should().Be(1, "CR-H043: parameterless Read() must run the lazy-init gate");
    }
}
