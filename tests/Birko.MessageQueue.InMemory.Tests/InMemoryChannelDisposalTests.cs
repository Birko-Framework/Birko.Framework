using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace Birko.MessageQueue.InMemory.Tests;

/// <summary>
/// Regressions for CR-H119: disposing the queue must tear down every destination's
/// dispatch loop / CancellationTokenSource, and RemoveSubscriber must dispose (not just
/// cancel) the CTS when the last subscriber leaves. The InMemory sources are compiled
/// into this test assembly via shared projitems, so the internal channel is testable directly.
/// </summary>
public class InMemoryChannelDisposalTests
{
    private static QueueMessage Msg(string body = "x") => new() { Body = body };

    private static async Task<bool> WaitForAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(10).ConfigureAwait(false);
        }

        return condition();
    }

    [Fact]
    public async Task Subscriber_ReceivesDispatchedMessage()
    {
        var channel = new InMemoryChannel(capacity: 10);
        var received = 0;
        channel.AddSubscriber("q", (_, _) => { Interlocked.Increment(ref received); return Task.CompletedTask; });

        await channel.WriteAsync("q", Msg());

        (await WaitForAsync(() => received == 1)).Should().BeTrue();
    }

    [Fact]
    public async Task Dispose_StopsDispatch_NoDeliveryAfterwards()
    {
        var channel = new InMemoryChannel(capacity: 10);
        var received = 0;
        channel.AddSubscriber("q", (_, _) => { Interlocked.Increment(ref received); return Task.CompletedTask; });

        channel.Dispose();

        // Writing after disposal starts a fresh (subscriber-less) destination — nothing is delivered.
        await channel.WriteAsync("q", Msg());
        await Task.Delay(100);

        received.Should().Be(0);
    }

    [Fact]
    public void Dispose_IsIdempotent_WithActiveSubscriber()
    {
        var channel = new InMemoryChannel(capacity: 10);
        channel.AddSubscriber("q", (_, _) => Task.CompletedTask);

        var act = () =>
        {
            channel.Dispose();
            channel.Dispose();
        };

        act.Should().NotThrow();
    }

    [Fact]
    public async Task RemoveSubscriber_ThenReAdd_RestartsDispatch()
    {
        // The last RemoveSubscriber cancels AND disposes the dispatch CTS; re-adding must
        // spin up a fresh loop. A double-dispose / use-after-dispose of the CTS would throw here.
        var channel = new InMemoryChannel(capacity: 10);
        var id = channel.AddSubscriber("q", (_, _) => Task.CompletedTask);
        channel.RemoveSubscriber("q", id);

        var received = 0;
        channel.AddSubscriber("q", (_, _) => { Interlocked.Increment(ref received); return Task.CompletedTask; });
        await channel.WriteAsync("q", Msg());

        (await WaitForAsync(() => received == 1)).Should().BeTrue();
    }

    [Fact]
    public void RemoveSubscriber_LastOne_DoesNotThrow()
    {
        var channel = new InMemoryChannel(capacity: 10);
        var id = channel.AddSubscriber("q", (_, _) => Task.CompletedTask);

        var act = () => channel.RemoveSubscriber("q", id);

        act.Should().NotThrow();
    }
}
