using FluentAssertions;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Birko.Data.Sync.Tests;

public class SyncQueueTests
{
    [Fact]
    public async Task EnqueueAsync_ExecutesOperation()
    {
        var queue = new SyncQueue();
        var executed = false;

        await queue.EnqueueAsync("scope", () =>
        {
            executed = true;
            return Task.FromResult(42);
        });

        executed.Should().BeTrue();
    }

    [Fact]
    public async Task EnqueueAsync_ReturnsSyncResult()
    {
        var queue = new SyncQueue();

        var result = await queue.EnqueueAsync("scope", () => Task.FromResult(42));

        result.Should().Be(42);
    }

    [Fact]
    public async Task EnqueueAsync_ConcurrentSameScope_Serializes()
    {
        var queue = new SyncQueue(maxConcurrentSyncs: 1);
        var running = 0;
        var maxConcurrent = 0;

        var tasks = new Task[3];
        for (var i = 0; i < 3; i++)
        {
            tasks[i] = queue.EnqueueAsync("scope", async () =>
            {
                var current = Interlocked.Increment(ref running);
                if (current > maxConcurrent)
                    Interlocked.Exchange(ref maxConcurrent, current);
                await Task.Delay(50);
                Interlocked.Decrement(ref running);
                return true;
            });
        }

        await Task.WhenAll(tasks);

        maxConcurrent.Should().Be(1);
    }

    [Fact]
    public void GetQueueLength_EmptyScope_ReturnsZero()
    {
        var queue = new SyncQueue();

        queue.GetQueueLength("nonexistent").Should().Be(0);
    }

    [Fact]
    public void GetAllQueueLengths_InitiallyEmpty()
    {
        var queue = new SyncQueue();

        queue.GetAllQueueLengths().Should().BeEmpty();
    }

    [Fact]
    public void Clear_RemovesAllQueues()
    {
        var queue = new SyncQueue();
        queue.Clear(); // Should not throw even if empty

        queue.GetAllQueueLengths().Should().BeEmpty();
    }

    [Fact]
    public void MaxConcurrentSyncs_Property_ReturnsConfiguredValue()
    {
        var queue = new SyncQueue(5);

        queue.MaxConcurrentSyncs.Should().Be(5);
    }

    [Fact]
    public async Task EnqueueAsync_CancellationToken_ThrowsOperationCanceled()
    {
        var queue = new SyncQueue(maxConcurrentSyncs: 1);
        var cts = new CancellationTokenSource();

        // Fill the semaphore
        var blockTask = queue.EnqueueAsync("scope", async () =>
        {
            await Task.Delay(5000);
            return true;
        });

        // Cancel immediately
        await cts.CancelAsync();

        var act = () => queue.EnqueueAsync("scope", () => Task.FromResult(true), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
