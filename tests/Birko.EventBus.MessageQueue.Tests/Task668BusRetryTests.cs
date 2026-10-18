using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Birko.EventBus;
using Birko.EventBus.MessageQueue;
using Birko.MessageQueue;
using Birko.MessageQueue.InMemory;
using Birko.MessageQueue.Redis;
using Birko.MessageQueue.Retry;
using FluentAssertions;
using StackExchange.Redis;
using Xunit;
using Xunit.Abstractions;

namespace Birko.EventBus.MessageQueue.Tests;

/// <summary>
/// TASK-668: DistributedEventBusOptions.RetryPolicy (Birko.RetryPolicy) and DeadLetterOptions, refused at TASK-174
/// (SH-L114), are applied by the bus. A failed delivery is retried until its x-delivery-count passes MaxRetries, then
/// dead-lettered to {topic}.dlq (or dropped and reported). In-memory retries by a delayed re-send; Redis by its own
/// redelivery of the pending entry (live, gated on <c>BIRKO_REDIS_HOST</c>).
/// </summary>
public class Task668BusRetryTests(ITestOutputHelper output)
{
    public sealed class T668Event : IEvent
    {
        public Guid EventGuid { get; set; } = Guid.NewGuid();
        public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
        public string Source { get; set; } = "t668";
    }

    private sealed class Handler(Func<int, bool> succeedsOnCall) : IEventHandler<T668Event>
    {
        public int Calls;

        public Task HandleAsync(T668Event @event, EventContext context, CancellationToken ct = default)
        {
            var call = Interlocked.Increment(ref Calls);
            return succeedsOnCall(call) ? Task.CompletedTask : throw new InvalidOperationException($"boom {call}");
        }
    }

    private static global::Birko.RetryPolicy Fast(int maxRetries) =>
        new() { MaxRetries = maxRetries, BaseDelay = TimeSpan.FromMilliseconds(10), UseExponentialBackoff = false };

    private static async Task<bool> Eventually(Func<bool> condition, int timeoutMs = 8000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (condition()) return true;
            await Task.Delay(20);
        }
        return condition();
    }

    [Fact]
    public async Task A_delivery_that_keeps_failing_is_retried_then_dead_lettered()
    {
        using var queue = new InMemoryMessageQueue();
        var options = new DistributedEventBusOptions { RetryPolicy = Fast(2), DeadLetterOptions = new DeadLetterOptions() };
        var topic = options.TopicConvention.GetTopic(typeof(T668Event));
        await using var bus = new DistributedEventBus(queue, options);
        var handler = new Handler(_ => false);
        bus.Subscribe(handler);
        await bus.SubscribeToTransportAsync<T668Event>();
        var dead = new ConcurrentQueue<QueueMessage>();
        using var dlq = await queue.Consumer.SubscribeAsync(topic + ".dlq", (m, _) => { dead.Enqueue(m); return Task.CompletedTask; });

        await bus.PublishAsync(new T668Event());

        (await Eventually(() => dead.Count == 1)).Should().BeTrue();
        handler.Calls.Should().Be(3, "the first delivery and two retries");
        var headers = dead.Single().Headers.Custom;
        headers["x-delivery-count"].Should().Be("3");
        headers[DistributedEventBus.OriginalDestinationHeader].Should().Be(topic);
        headers[DistributedEventBus.DeadLetterReasonHeader].Should().Contain("boom 3");
    }

    [Fact]
    public async Task A_delivery_that_succeeds_on_a_retry_is_not_dead_lettered()
    {
        using var queue = new InMemoryMessageQueue();
        var options = new DistributedEventBusOptions { RetryPolicy = Fast(3), DeadLetterOptions = new DeadLetterOptions() };
        await using var bus = new DistributedEventBus(queue, options);
        var handler = new Handler(call => call == 3);
        bus.Subscribe(handler);
        await bus.SubscribeToTransportAsync<T668Event>();
        var dead = 0;
        using var dlq = await queue.Consumer.SubscribeAsync(options.TopicConvention.GetTopic(typeof(T668Event)) + ".dlq",
            (_, _) => { Interlocked.Increment(ref dead); return Task.CompletedTask; });

        await bus.PublishAsync(new T668Event());

        (await Eventually(() => handler.Calls == 3)).Should().BeTrue();
        await Task.Delay(200);
        handler.Calls.Should().Be(3);
        dead.Should().Be(0);
    }

    [Fact]
    public async Task Without_a_dead_letter_destination_a_spent_delivery_is_dropped_and_reported()
    {
        using var queue = new InMemoryMessageQueue();
        await using var bus = new DistributedEventBus(queue, new DistributedEventBusOptions { RetryPolicy = Fast(1) });
        var handler = new Handler(_ => false);
        bus.Subscribe(handler);
        var dropped = new ConcurrentQueue<string>();
        bus.MessageDropped += (_, reason) => dropped.Enqueue(reason);
        await bus.SubscribeToTransportAsync<T668Event>();

        await bus.PublishAsync(new T668Event());

        (await Eventually(() => dropped.Count == 1)).Should().BeTrue();
        handler.Calls.Should().Be(2);
        dropped.Single().Should().Contain("retries are spent");
    }

    [Fact]
    public void A_redis_queue_without_a_consumer_group_cannot_count_deliveries_and_is_refused()
    {
        using var queue = new RedisStreamQueue(new RedisStreamSettings("localhost"));
        var create = () => new DistributedEventBus(queue, new DistributedEventBusOptions { RetryPolicy = Fast(1) });
        create.Should().Throw<NotSupportedException>().WithMessage("*consumer group*");
    }

    [Fact]
    public async Task On_redis_the_pending_entry_is_redelivered_then_dead_lettered()
    {
        var host = Environment.GetEnvironmentVariable("BIRKO_REDIS_HOST");
        if (string.IsNullOrWhiteSpace(host))
        {
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
                throw new InvalidOperationException("BIRKO_REDIS_HOST is not set but BIRKO_REQUIRE_LIVE is.");
            output.WriteLine("SKIPPED: BIRKO_REDIS_HOST not set.");
            return;
        }

        var settings = new RedisStreamSettings(host)
        {
            StreamPrefix = "birko:t668bus:" + Guid.NewGuid().ToString("N"),
            ConsumerGroup = "g",
            BlockMilliseconds = 50,
            PendingRetryMilliseconds = 200,
        };
        using var queue = new RedisStreamQueue(settings);
        var options = new DistributedEventBusOptions
        {
            RetryPolicy = Fast(1),
            DeadLetterOptions = new DeadLetterOptions(),
            ConsumerOptions = new ConsumerOptions { FromBeginning = true },
        };
        var topic = options.TopicConvention.GetTopic(typeof(T668Event));
        await using var bus = new DistributedEventBus(queue, options);
        var handler = new Handler(_ => false);
        bus.Subscribe(handler);
        await bus.SubscribeToTransportAsync<T668Event>();
        var mux = await ConnectionMultiplexer.ConnectAsync(host);
        var db = mux.GetDatabase();

        await bus.PublishAsync(new T668Event());

        var dlqKey = settings.GetStreamKey(topic + ".dlq");
        (await EventuallyAsync(async () => await db.StreamLengthAsync(dlqKey) == 1)).Should().BeTrue("dead-lettered after the redelivery");
        handler.Calls.Should().Be(2, "the first delivery and one redelivery");
        (await EventuallyAsync(async () => (await db.StreamPendingAsync(settings.GetStreamKey(topic), "g")).PendingMessageCount == 0))
            .Should().BeTrue("the dead-lettered entry is acknowledged on the source stream");
        await db.KeyDeleteAsync(new RedisKey[] { dlqKey, settings.GetStreamKey(topic) });
    }

    private static async Task<bool> EventuallyAsync(Func<Task<bool>> condition, int timeoutMs = 10000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (await condition()) return true;
            await Task.Delay(50);
        }
        return await condition();
    }
}
