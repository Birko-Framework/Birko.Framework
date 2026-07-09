using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.RavenDB.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.RavenDB.Tests.Stores;

/// <summary>
/// CR-H077: RavenDBStore/AsyncRavenDBStore overrode the PUBLIC Read(Guid)/Read()/ReadAsync(Guid)
/// methods and queried the document store directly, skipping the base EnsureInitialized(Async) gate.
/// As the first operation on a fresh store this meant InitCore (which creates the database) never
/// ran. These probes override InitCore(Async) to record whether the gate fired — no live server
/// required — and confirm the read paths now initialize (and the async path observes cancellation).
/// </summary>
public class RavenDBStoreLazyInitTests
{
    private class Doc : AbstractModel { }

    private class ProbeStore : RavenDBStore<Doc>
    {
        public int InitCount;
        protected override void InitCore() => InitCount++;   // skip real store creation
    }

    private class AsyncProbeStore : AsyncRavenDBStore<Doc>
    {
        public int InitCount;
        protected override Task InitCoreAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            InitCount++;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void Read_ByGuid_TriggersLazyInit()
    {
        var store = new ProbeStore();
        store.Read(Guid.NewGuid());
        store.InitCount.Should().Be(1);
    }

    [Fact]
    public void Read_All_TriggersLazyInit()
    {
        var store = new ProbeStore();
        _ = store.Read().ToList();
        store.InitCount.Should().Be(1);
    }

    [Fact]
    public async Task ReadAsync_ByGuid_TriggersLazyInit()
    {
        var store = new AsyncProbeStore();
        await store.ReadAsync(Guid.NewGuid());
        store.InitCount.Should().Be(1);
    }

    [Fact]
    public async Task ReadAsync_ByGuid_ObservesCancellation()
    {
        var store = new AsyncProbeStore();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await store.Invoking(s => s.ReadAsync(Guid.NewGuid(), cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }
}
