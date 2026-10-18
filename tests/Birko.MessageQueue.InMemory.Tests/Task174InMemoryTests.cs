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
    [Theory]
    [InlineData("ttl")]
    [InlineData("priority")]
    public async Task SH_L108_SH_L109_a_message_option_the_transport_cannot_honour_is_refused(string option)
    {
        using var queue = new InMemoryMessageQueue();
        var message = option == "ttl" ? new QueueMessage { Body = "x", TimeToLive = TimeSpan.FromSeconds(5) } : new QueueMessage { Body = "x", Priority = 3 };
        var send = () => queue.Producer.SendAsync("d", message, CancellationToken.None);
        await send.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task SH_L110_a_prefetch_count_is_refused()
    {
        using var queue = new InMemoryMessageQueue();
        var subscribe = () => queue.Consumer.SubscribeAsync("d", (_, _) => Task.CompletedTask, new ConsumerOptions { PrefetchCount = 10 });
        await subscribe.Should().ThrowAsync<NotSupportedException>();
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
