using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Birko.MessageQueue.Serialization;
using FluentAssertions;
using Xunit;

namespace Birko.MessageQueue.InMemory.Tests;

/// <summary>
/// CR-M201: delayed SendAsync is fire-and-forget — its fault is now observed and the behavior is
/// documented as best-effort (a cancelled delayed send no longer surfaces or throws from SendAsync).
/// CR-M202: RejectAsync(requeue: true) previously discarded the message (empty if-body, destination
/// unknown); it now tracks the originating destination and writes the message back for redelivery.
/// The InMemory sources compile into this assembly via projitems, so the internal ctors are reachable.
/// </summary>
public class InMemoryProducerConsumerTests
{
    private static async Task<bool> WaitForAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (condition()) return true;
            await Task.Delay(10).ConfigureAwait(false);
        }
        return condition();
    }

    private static (InMemoryChannel channel, InMemoryProducer producer, InMemoryConsumer consumer) NewQueue()
    {
        var channel = new InMemoryChannel(capacity: 100);
        var serializer = new JsonMessageSerializer();
        return (channel, new InMemoryProducer(channel, serializer), new InMemoryConsumer(channel, serializer));
    }

    // ---- CR-M202 ----

    [Fact]
    public async Task RejectAsync_Requeue_RedeliversMessage()
    {
        var (_, producer, consumer) = NewQueue();
        var deliveries = new ConcurrentBag<Guid>();

        await consumer.SubscribeAsync("q",
            (msg, ct) => { deliveries.Add(msg.Guid); return Task.CompletedTask; },
            new ConsumerOptions { AckMode = MessageAckMode.ManualAck });

        var message = new QueueMessage { Body = "x" };
        await producer.SendAsync("q", message, CancellationToken.None);
        (await WaitForAsync(() => deliveries.Count >= 1)).Should().BeTrue();

        await consumer.RejectAsync(message.Guid, requeue: true);

        (await WaitForAsync(() => deliveries.Count >= 2)).Should().BeTrue("requeue must redeliver, not discard (CR-M202)");
    }

    [Fact]
    public async Task RejectAsync_NoRequeue_DoesNotRedeliver()
    {
        var (_, producer, consumer) = NewQueue();
        var deliveries = new ConcurrentBag<Guid>();

        await consumer.SubscribeAsync("q",
            (msg, ct) => { deliveries.Add(msg.Guid); return Task.CompletedTask; },
            new ConsumerOptions { AckMode = MessageAckMode.ManualAck });

        var message = new QueueMessage { Body = "x" };
        await producer.SendAsync("q", message, CancellationToken.None);
        (await WaitForAsync(() => deliveries.Count >= 1)).Should().BeTrue();

        await consumer.RejectAsync(message.Guid, requeue: false);
        await Task.Delay(100);

        deliveries.Count.Should().Be(1);
    }

    // ---- CR-M201 ----

    [Fact]
    public async Task DelayedSend_EventuallyDelivers()
    {
        var (_, producer, consumer) = NewQueue();
        var received = 0;
        await consumer.SubscribeAsync("q", (_, _) => { Interlocked.Increment(ref received); return Task.CompletedTask; });

        await producer.SendAsync("q", new QueueMessage { Body = "x", Delay = TimeSpan.FromMilliseconds(50) }, CancellationToken.None);

        (await WaitForAsync(() => received == 1)).Should().BeTrue();
    }

    [Fact]
    public async Task DelayedSend_WithAnAlreadyCancelledToken_IsCancelled_AndNotDelivered()
    {
        var (_, producer, consumer) = NewQueue();
        var received = 0;
        await consumer.SubscribeAsync("q", (_, _) => { Interlocked.Increment(ref received); return Task.CompletedTask; });

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // TASK-155 (SH-M112) inverted this: a call whose token is already cancelled is cancelled, as a send without a
        // delay is. It used to return quietly. What the token does NOT do any more is cancel the delayed delivery after
        // the call has returned: that runs on the producer's lifetime (Task155InMemoryTests).
        await producer.Invoking(p => p.SendAsync("q", new QueueMessage { Body = "x", Delay = TimeSpan.FromMilliseconds(50) }, cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        await Task.Delay(100);
        received.Should().Be(0, "a cancelled delayed send is not delivered");
    }
}
