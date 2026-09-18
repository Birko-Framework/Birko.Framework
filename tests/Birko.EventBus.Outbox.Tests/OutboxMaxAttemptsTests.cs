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
/// CR-H115: OutboxOptions.MaxAttempts was never enforced — MarkFailedAsync got no cap and
/// InMemoryOutboxStore hardcoded ">= 5", so configuring MaxAttempts did nothing. The cap is now
/// plumbed through MarkFailedAsync and honored by the store + processor.
/// </summary>
public class OutboxMaxAttemptsTests
{
    private class TestEvent : IEvent
    {
        public Guid EventId { get; set; } = Guid.NewGuid();
        public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
        public string Source { get; set; } = "test";
    }

    private sealed class ThrowingBus : IEventBus
    {
        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : IEvent
            => throw new InvalidOperationException("publish failed");
        public IEventSubscription Subscribe<TEvent>(IEventHandler<TEvent> handler) where TEvent : IEvent
            => throw new NotSupportedException();
        public void Dispose() { }
    }

    [Fact]
    public async Task Store_MarkFailed_HonorsMaxAttempts()
    {
        var store = new InMemoryOutboxStore();
        var entry = new OutboxEntry { EventType = "x", Payload = "{}" };
        await store.SaveAsync(entry);

        await store.MarkFailedAsync(entry.Id, "e1", maxAttempts: 3, CancellationToken.None);
        entry.Status.Should().Be(OutboxStatus.Pending);   // 1/3

        await store.MarkFailedAsync(entry.Id, "e2", maxAttempts: 3, CancellationToken.None);
        entry.Status.Should().Be(OutboxStatus.Pending);   // 2/3

        await store.MarkFailedAsync(entry.Id, "e3", maxAttempts: 3, CancellationToken.None);
        entry.Status.Should().Be(OutboxStatus.Failed);    // 3/3 — cap reached
        entry.Attempts.Should().Be(3);
    }

    [Fact]
    public async Task Processor_FailsEntry_AtConfiguredMaxAttempts_NotHardcodedFive()
    {
        var store = new InMemoryOutboxStore();
        var serializer = new JsonMessageSerializer();
        var options = new OutboxOptions { MaxAttempts = 2, BatchSize = 10 };
        var processor = new OutboxProcessor(store, new ThrowingBus(), options, serializer);

        var evt = new TestEvent();
        var entry = new OutboxEntry
        {
            EventType = typeof(TestEvent).AssemblyQualifiedName!,
            Payload = serializer.Serialize(evt),
        };
        await store.SaveAsync(entry);

        // First batch: publish throws → Attempts=1, still Pending (below cap of 2).
        await processor.ProcessBatchAsync();
        entry.Attempts.Should().Be(1);
        entry.Status.Should().Be(OutboxStatus.Pending);

        // Second batch: Attempts=2 → reaches cap → Failed (would still be Pending under the old
        // hardcoded 5).
        await processor.ProcessBatchAsync();
        entry.Attempts.Should().Be(2);
        entry.Status.Should().Be(OutboxStatus.Failed);
    }
}
