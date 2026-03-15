using System;
using System.Threading.Tasks;
using Birko.EventBus.Deduplication;
using Birko.EventBus.Local;
using Birko.EventBus.Tests.TestResources;
using FluentAssertions;
using Xunit;

namespace Birko.EventBus.Tests
{
    public class DeduplicationTests
    {
        [Fact]
        public async Task InMemoryStore_ExistsAsync_ReturnsFalseForNew()
        {
            var store = new InMemoryDeduplicationStore();
            var result = await store.ExistsAsync(Guid.NewGuid());
            result.Should().BeFalse();
        }

        [Fact]
        public async Task InMemoryStore_ExistsAsync_ReturnsTrueAfterMark()
        {
            var store = new InMemoryDeduplicationStore();
            var id = Guid.NewGuid();

            await store.MarkProcessedAsync(id);
            var result = await store.ExistsAsync(id);

            result.Should().BeTrue();
        }

        [Fact]
        public async Task DeduplicationBehavior_SkipsDuplicate()
        {
            var store = new InMemoryDeduplicationStore();
            var behavior = new DeduplicationBehavior(store);
            var handler = new OrderPlacedHandler();

            using var bus = new InProcessEventBus(behaviors: [behavior]);
            bus.Subscribe(handler);

            var evt = new OrderPlaced(Guid.NewGuid(), 1m);

            await bus.PublishAsync(evt);
            await bus.PublishAsync(evt); // Same EventId — duplicate

            handler.ReceivedEvents.Should().ContainSingle();
        }

        [Fact]
        public async Task DeduplicationBehavior_AllowsDifferentEvents()
        {
            var store = new InMemoryDeduplicationStore();
            var behavior = new DeduplicationBehavior(store);
            var handler = new OrderPlacedHandler();

            using var bus = new InProcessEventBus(behaviors: [behavior]);
            bus.Subscribe(handler);

            await bus.PublishAsync(new OrderPlaced(Guid.NewGuid(), 1m));
            await bus.PublishAsync(new OrderPlaced(Guid.NewGuid(), 2m)); // Different EventId

            handler.ReceivedEvents.Should().HaveCount(2);
        }
    }
}
