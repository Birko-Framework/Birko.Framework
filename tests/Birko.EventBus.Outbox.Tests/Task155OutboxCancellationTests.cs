using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.EventBus;
using Birko.EventBus.Outbox;
using Birko.EventBus.Outbox.Publishing;
using Birko.EventBus.Outbox.Stores;
using Birko.MessageQueue.Serialization;
using FluentAssertions;
using Xunit;

namespace Birko.EventBus.Outbox.Tests;

/// <summary>
/// TASK-155 (SH-M121): a shutdown while the outbox publishes is not a failed attempt. It was recorded as one with the
/// already-cancelled token, so a good event moved towards Failed and, on a real store, the record call threw too.
/// </summary>
public class Task155OutboxCancellationTests
{
    private class TestEvent : IEvent
    {
        public Guid EventGuid { get; set; } = Guid.NewGuid();
        public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
        public string Source { get; set; } = "test";
    }

    /// <summary>Publishes until the host stops, then observes the stopping token as a real transport does.</summary>
    private sealed class StoppingBus(CancellationTokenSource stop) : IEventBus
    {
        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : IEvent
        {
            stop.Cancel();
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
        public IEventSubscription Subscribe<TEvent>(IEventHandler<TEvent> handler) where TEvent : IEvent => throw new NotSupportedException();
        public void Dispose() { }
    }

    [Fact]
    public async Task SH_M121_a_publish_cancelled_by_shutdown_is_not_counted_as_a_failure()
    {
        var store = new InMemoryOutboxStore();
        var serializer = new JsonMessageSerializer();
        using var stop = new CancellationTokenSource();
        var processor = new OutboxProcessor(store, new StoppingBus(stop), new OutboxOptions { MaxAttempts = 1, BatchSize = 10 }, serializer);
        var entry = new OutboxEntry { EventType = typeof(TestEvent).AssemblyQualifiedName!, Payload = serializer.Serialize(new TestEvent()) };
        await store.SaveAsync(entry);

        var run = () => processor.ProcessBatchAsync(stop.Token);
        await run.Should().NotThrowAsync<InvalidOperationException>();

        entry.Attempts.Should().Be(0, "a shutdown is not an attempt");
        entry.Status.Should().NotBe(OutboxStatus.Failed, "with MaxAttempts = 1 one counted attempt would have failed it for good");
    }
}
