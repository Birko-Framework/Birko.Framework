using System;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace Birko.MessageQueue.InMemory.Tests;

/// <summary>
/// CR-H119: disposing the queue must dispose the shared channel (tear down dispatch
/// loops + CancellationTokenSources), even when a subscription was left open.
/// </summary>
public class InMemoryMessageQueueDisposalTests
{
    [Fact]
    public async Task Dispose_WithOpenSubscription_DoesNotThrow()
    {
        var queue = new InMemoryMessageQueue();
        await queue.ConnectAsync();

        // Leave a subscription open — the queue must still tear the channel down cleanly.
        await queue.Consumer.SubscribeAsync("q", (_, _) => Task.CompletedTask);

        var act = queue.Dispose;

        act.Should().NotThrow();
    }

    [Fact]
    public async Task Dispose_IsIdempotent()
    {
        var queue = new InMemoryMessageQueue();
        await queue.ConnectAsync();

        queue.Dispose();
        var act = queue.Dispose;

        act.Should().NotThrow();
        queue.IsConnected.Should().BeFalse();
    }
}
