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
/// TASK-174 on the in-memory transport: SH-L108 / SH-L109 / SH-L110 (TimeToLive, Priority and PrefetchCount ignored;
/// decided by the user: refuse until implemented) and SH-L113 (a requeued message carried no delivery count).
/// </summary>
public class Task174InMemoryTests
{
    // SH-L108 (TimeToLive) and SH-L110 (PrefetchCount) were refused here until TASK-668 implemented them; their tests
    // were inverted into Task668InMemoryTests. Priority stays refused (decided by the user 2026-10-18).
    [Fact]
    public async Task SH_L109_a_priority_is_refused()
    {
        using var queue = new InMemoryMessageQueue();
        var send = () => queue.Producer.SendAsync("d", new QueueMessage { Body = "x", Priority = 3 }, CancellationToken.None);
        await send.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task SH_L113_a_requeued_message_carries_its_delivery_count()
    {
        using var queue = new InMemoryMessageQueue();
        var counts = new ConcurrentQueue<string?>();
        using var sub = await queue.Consumer.SubscribeAsync("d", (m, _) =>
        {
            counts.Enqueue(m.Headers.Custom.TryGetValue("x-delivery-count", out var c) ? c : null);
            return Task.CompletedTask;
        }, new ConsumerOptions { AckMode = MessageAckMode.ManualAck });
        var message = new QueueMessage { Body = "x" };
        await queue.Producer.SendAsync("d", message, CancellationToken.None);
        var deadline = Environment.TickCount64 + 3000;
        while (counts.IsEmpty && Environment.TickCount64 < deadline) await Task.Delay(10);

        await queue.Consumer.RejectAsync(message.Guid, requeue: true);
        while (counts.Count < 2 && Environment.TickCount64 < deadline) await Task.Delay(10);

        counts.ToArray().Should().Equal(null, "2");
    }
}
