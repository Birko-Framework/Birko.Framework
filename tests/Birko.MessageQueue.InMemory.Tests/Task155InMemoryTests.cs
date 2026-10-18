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
/// TASK-155 — the in-memory transport: SH-M109 (a handler failure vanished), SH-M110 (a ManualAck failure dropped the
/// pending entry, so a requeue found nothing), SH-M111 (a disposed consumer kept receiving) and SH-M112 (a delayed send
/// was cancelled by the caller's token after the call had returned).
/// </summary>
public class Task155InMemoryTests
{
    private static async Task<bool> Eventually(Func<bool> condition, int timeoutMs = 3000)
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
    public async Task SH_M109_a_failing_handler_is_reported()
    {
        using var queue = new InMemoryMessageQueue();
        var consumer = (InMemoryConsumer)queue.Consumer;
        var reported = new ConcurrentBag<string>();
        consumer.OnHandlerError = (ex, m) => { reported.Add(m.Body + ":" + ex.Message); return Task.CompletedTask; };
        using var sub = await consumer.SubscribeAsync("d", (_, _) => throw new InvalidOperationException("boom"));

        await queue.Producer.SendAsync("d", new QueueMessage { Body = "x" }, CancellationToken.None);

        (await Eventually(() => !reported.IsEmpty)).Should().BeTrue("the failure is observable, not swallowed");
        reported.Should().ContainSingle().Which.Should().Be("x:boom");
    }

    [Fact]
    public async Task SH_M110_a_ManualAck_message_whose_handler_failed_can_still_be_requeued()
    {
        using var queue = new InMemoryMessageQueue();
        var attempts = 0;
        using var sub = await queue.Consumer.SubscribeAsync("d", (_, _) =>
        {
            if (Interlocked.Increment(ref attempts) == 1) throw new InvalidOperationException("first try fails");
            return Task.CompletedTask;
        }, new ConsumerOptions { AckMode = MessageAckMode.ManualAck });

        var message = new QueueMessage { Body = "x" };
        await queue.Producer.SendAsync("d", message, CancellationToken.None);
        (await Eventually(() => Volatile.Read(ref attempts) == 1)).Should().BeTrue();

        await queue.Consumer.RejectAsync(message.Guid, requeue: true);
        (await Eventually(() => Volatile.Read(ref attempts) == 2)).Should().BeTrue("the requeue redelivers the failed message");
    }

    [Fact]
    public async Task SH_M111_a_disposed_consumer_no_longer_receives()
    {
        using var queue = new InMemoryMessageQueue();
        var received = 0;
        await queue.Consumer.SubscribeAsync("d", (_, _) => { Interlocked.Increment(ref received); return Task.CompletedTask; });
        await queue.Producer.SendAsync("d", new QueueMessage { Body = "before" }, CancellationToken.None);
        (await Eventually(() => Volatile.Read(ref received) == 1)).Should().BeTrue();

        queue.Consumer.Dispose();
        await queue.Producer.SendAsync("d", new QueueMessage { Body = "after" }, CancellationToken.None);
        await Task.Delay(200);
        Volatile.Read(ref received).Should().Be(1, "a disposed consumer's handlers are removed from the channel");
    }

    [Fact]
    public async Task SH_M112_a_delayed_send_outlives_the_callers_token()
    {
        using var queue = new InMemoryMessageQueue();
        var received = new ConcurrentBag<string>();
        using var sub = await queue.Consumer.SubscribeAsync("d", (m, _) => { received.Add(m.Body); return Task.CompletedTask; });

        using (var request = new CancellationTokenSource())
        {
            await queue.Producer.SendAsync("d", new QueueMessage { Body = "later", Delay = TimeSpan.FromMilliseconds(200) }, request.Token);
            request.Cancel(); // the request ends right after the send returned
        }

        (await Eventually(() => received.Contains("later"))).Should().BeTrue("a send that reported success is delivered");
    }
}
