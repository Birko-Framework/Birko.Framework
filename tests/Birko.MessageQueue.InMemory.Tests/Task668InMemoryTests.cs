using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Birko.MessageQueue;
using Birko.MessageQueue.InMemory;
using FluentAssertions;
using Xunit;

namespace Birko.MessageQueue.InMemory.Tests;

/// <summary>
/// TASK-668 on the in-memory transport: TimeToLive and PrefetchCount, refused at TASK-174 (SH-L108 / SH-L110), are
/// honoured. An expired message is dropped at dispatch, measured on an injected clock; PrefetchCount is how many of a
/// subscription's handlers run at once.
/// </summary>
public class Task668InMemoryTests
{
    private sealed class ManualClock(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = start;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static async Task<bool> Eventually(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (condition()) return true;
            await Task.Delay(10);
        }
        return condition();
    }

    [Fact]
    public async Task An_expired_message_is_dropped_and_a_live_one_delivered()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero));
        using var queue = new InMemoryMessageQueue(new InMemoryMessageQueueOptions { Clock = clock });
        await queue.Producer.SendAsync("d", new QueueMessage { Body = "expired", CreatedAt = clock.Now, TimeToLive = TimeSpan.FromSeconds(5) }, CancellationToken.None);
        await queue.Producer.SendAsync("d", new QueueMessage { Body = "live", CreatedAt = clock.Now, TimeToLive = TimeSpan.FromSeconds(60) }, CancellationToken.None);
        await queue.Producer.SendAsync("d", new QueueMessage { Body = "forever", CreatedAt = clock.Now }, CancellationToken.None);
        clock.Now += TimeSpan.FromSeconds(10);

        var received = new ConcurrentQueue<string>();
        using var sub = await queue.Consumer.SubscribeAsync("d", (m, _) => { received.Enqueue(m.Body); return Task.CompletedTask; });

        (await Eventually(() => received.Count >= 2)).Should().BeTrue();
        await Task.Delay(100);
        received.Should().Equal("live", "forever");
    }

    [Fact]
    public async Task A_prefetch_count_bounds_the_handlers_in_flight()
    {
        using var queue = new InMemoryMessageQueue();
        var gate = new TaskCompletionSource();
        int inFlight = 0, peak = 0, handled = 0;
        using var sub = await queue.Consumer.SubscribeAsync("d", async (_, _) =>
        {
            var now = Interlocked.Increment(ref inFlight);
            InterlockedMax(ref peak, now);
            await gate.Task;
            Interlocked.Decrement(ref inFlight);
            Interlocked.Increment(ref handled);
        }, new ConsumerOptions { PrefetchCount = 3 });

        for (var i = 0; i < 6; i++) await queue.Producer.SendAsync("d", new QueueMessage { Body = i.ToString() }, CancellationToken.None);

        (await Eventually(() => Volatile.Read(ref inFlight) == 3)).Should().BeTrue("three handlers run at once");
        await Task.Delay(150);
        Volatile.Read(ref peak).Should().Be(3, "never more than PrefetchCount");
        gate.SetResult();
        (await Eventually(() => Volatile.Read(ref handled) == 6)).Should().BeTrue();
        Volatile.Read(ref peak).Should().Be(3);
    }

    [Fact]
    public async Task The_default_prefetch_handles_one_message_at_a_time()
    {
        using var queue = new InMemoryMessageQueue();
        var gate = new TaskCompletionSource();
        int inFlight = 0, peak = 0, handled = 0;
        using var sub = await queue.Consumer.SubscribeAsync("d", async (_, _) =>
        {
            InterlockedMax(ref peak, Interlocked.Increment(ref inFlight));
            await gate.Task;
            Interlocked.Decrement(ref inFlight);
            Interlocked.Increment(ref handled);
        });

        for (var i = 0; i < 3; i++) await queue.Producer.SendAsync("d", new QueueMessage { Body = i.ToString() }, CancellationToken.None);
        (await Eventually(() => Volatile.Read(ref inFlight) == 1)).Should().BeTrue();
        await Task.Delay(100);
        gate.SetResult();
        (await Eventually(() => Volatile.Read(ref handled) == 3)).Should().BeTrue();
        Volatile.Read(ref peak).Should().Be(1);
    }

    [Fact]
    public async Task A_prefetch_below_one_is_refused()
    {
        using var queue = new InMemoryMessageQueue();
        var subscribe = () => queue.Consumer.SubscribeAsync("d", (_, _) => Task.CompletedTask, new ConsumerOptions { PrefetchCount = 0 });
        await subscribe.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void The_transport_resends_failed_deliveries_for_a_retry_rule()
    {
        using var queue = new InMemoryMessageQueue();
        ((IRedeliveryCapability)queue).GetRedeliveryMode(null).Should().Be(RedeliveryMode.Resend);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int seen;
        while ((seen = Volatile.Read(ref target)) < value && Interlocked.CompareExchange(ref target, value, seen) != seen) { }
    }
}
