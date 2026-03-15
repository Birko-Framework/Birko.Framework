using System;
using System.Linq;
using System.Threading.Tasks;
using Birko.EventBus.Local;
using Birko.EventBus.Outbox;
using Birko.EventBus.Outbox.Publishing;
using Birko.EventBus.Outbox.Stores;
using Birko.EventBus.Tests.TestResources;
using FluentAssertions;
using Xunit;

namespace Birko.EventBus.Tests.Outbox
{
    public class OutboxProcessorTests
    {
        [Fact]
        public async Task ProcessBatchAsync_PublishesPendingEntries()
        {
            var store = new InMemoryOutboxStore();
            var handler = new OrderPlacedHandler();
            using var innerBus = new InProcessEventBus();
            innerBus.Subscribe(handler);

            // Write event to outbox via OutboxEventBus
            using var outboxBus = new OutboxEventBus(innerBus, store);
            var evt = new OrderPlaced(Guid.NewGuid(), 42m);
            await outboxBus.PublishAsync(evt);

            // Process — should publish via inner bus
            var processor = new OutboxProcessor(store, innerBus);
            var count = await processor.ProcessBatchAsync();

            count.Should().Be(1);
            handler.ReceivedEvents.Should().ContainSingle()
                .Which.OrderId.Should().Be(evt.OrderId);
        }

        [Fact]
        public async Task ProcessBatchAsync_MarksAsPublished()
        {
            var store = new InMemoryOutboxStore();
            using var innerBus = new InProcessEventBus();
            using var outboxBus = new OutboxEventBus(innerBus, store);

            await outboxBus.PublishAsync(new OrderPlaced(Guid.NewGuid(), 1m));

            var processor = new OutboxProcessor(store, innerBus);
            await processor.ProcessBatchAsync();

            store.GetAll().Should().ContainSingle()
                .Which.Status.Should().Be(OutboxStatus.Published);
        }

        [Fact]
        public async Task ProcessBatchAsync_NoPending_ReturnsZero()
        {
            var store = new InMemoryOutboxStore();
            using var innerBus = new InProcessEventBus();

            var processor = new OutboxProcessor(store, innerBus);
            var count = await processor.ProcessBatchAsync();

            count.Should().Be(0);
        }

        [Fact]
        public async Task ProcessBatchAsync_MultiplePending_PublishesAll()
        {
            var store = new InMemoryOutboxStore();
            var handler = new OrderPlacedHandler();
            using var innerBus = new InProcessEventBus();
            innerBus.Subscribe(handler);
            using var outboxBus = new OutboxEventBus(innerBus, store);

            await outboxBus.PublishAsync(new OrderPlaced(Guid.NewGuid(), 1m));
            await outboxBus.PublishAsync(new OrderPlaced(Guid.NewGuid(), 2m));
            await outboxBus.PublishAsync(new OrderPlaced(Guid.NewGuid(), 3m));

            var processor = new OutboxProcessor(store, innerBus);
            var count = await processor.ProcessBatchAsync();

            count.Should().Be(3);
            handler.ReceivedEvents.Should().HaveCount(3);
            store.GetAll().Should().OnlyContain(e => e.Status == OutboxStatus.Published);
        }

        [Fact]
        public async Task ProcessBatchAsync_InvalidType_MarksAsFailed()
        {
            var store = new InMemoryOutboxStore();
            using var innerBus = new InProcessEventBus();

            // Manually insert an entry with bad type
            await store.SaveAsync(new OutboxEntry
            {
                EventId = Guid.NewGuid(),
                EventType = "NonExistent.Type, NoAssembly",
                Payload = "{}",
                Source = "test"
            });

            var processor = new OutboxProcessor(store, innerBus);
            await processor.ProcessBatchAsync();

            store.GetAll().Should().ContainSingle()
                .Which.LastError.Should().Contain("Cannot resolve type");
        }

        [Fact]
        public async Task CleanupAsync_RemovesOldEntries()
        {
            var store = new InMemoryOutboxStore();
            using var innerBus = new InProcessEventBus();

            var old = new OutboxEntry
            {
                EventId = Guid.NewGuid(),
                EventType = "Test",
                Payload = "{}",
                Source = "test",
                Status = OutboxStatus.Published,
                CreatedAt = DateTime.UtcNow.AddDays(-10)
            };
            await store.SaveAsync(old);

            var processor = new OutboxProcessor(store, innerBus, new OutboxOptions { RetentionPeriod = TimeSpan.FromDays(7) });
            await processor.CleanupAsync();

            store.GetAll().Should().BeEmpty();
        }

        [Fact]
        public async Task EndToEnd_OutboxEventBus_ThenProcess_ThenVerify()
        {
            // Full flow: publish via OutboxEventBus → process → handler receives event
            var store = new InMemoryOutboxStore();
            var handler = new ContextCapturingHandler();
            using var innerBus = new InProcessEventBus();
            innerBus.Subscribe(handler);

            using var outboxBus = new OutboxEventBus(innerBus, store);

            var correlationId = Guid.NewGuid();
            var evt = new OrderPlaced(Guid.NewGuid(), 100m) { CorrelationId = correlationId };

            // Step 1: Publish (goes to outbox, not inner bus)
            await outboxBus.PublishAsync(evt);
            handler.CapturedContext.Should().BeNull();

            // Step 2: Process outbox
            var processor = new OutboxProcessor(store, innerBus);
            await processor.ProcessBatchAsync();

            // Step 3: Verify handler received the event
            handler.CapturedContext.Should().NotBeNull();
            handler.CapturedContext!.EventId.Should().Be(evt.EventId);
        }
    }
}
