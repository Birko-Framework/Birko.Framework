using System;
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
    public class OutboxEventBusTests
    {
        [Fact]
        public async Task PublishAsync_WritesToOutboxStore()
        {
            var store = new InMemoryOutboxStore();
            using var inner = new InProcessEventBus();
            using var outbox = new OutboxEventBus(inner, store);

            var evt = new OrderPlaced(Guid.NewGuid(), 99.99m);
            await outbox.PublishAsync(evt);

            var entries = store.GetAll();
            entries.Should().ContainSingle();
            entries[0].EventId.Should().Be(evt.EventId);
            entries[0].EventType.Should().Contain("OrderPlaced");
            entries[0].Source.Should().Be("orders");
            entries[0].Status.Should().Be(OutboxStatus.Pending);
        }

        [Fact]
        public async Task PublishAsync_DoesNotPublishToInnerBus()
        {
            var store = new InMemoryOutboxStore();
            var handler = new OrderPlacedHandler();
            using var inner = new InProcessEventBus();
            inner.Subscribe(handler);
            using var outbox = new OutboxEventBus(inner, store);

            await outbox.PublishAsync(new OrderPlaced(Guid.NewGuid(), 1m));

            handler.ReceivedEvents.Should().BeEmpty(); // Not published yet — only in outbox
        }

        [Fact]
        public async Task PublishAsync_SerializesPayload()
        {
            var store = new InMemoryOutboxStore();
            using var inner = new InProcessEventBus();
            using var outbox = new OutboxEventBus(inner, store);

            var orderId = Guid.NewGuid();
            await outbox.PublishAsync(new OrderPlaced(orderId, 42m));

            var entries = store.GetAll();
            entries[0].Payload.Should().Contain(orderId.ToString());
        }

        [Fact]
        public void Subscribe_DelegatesToInnerBus()
        {
            var store = new InMemoryOutboxStore();
            var handler = new OrderPlacedHandler();
            using var inner = new InProcessEventBus();
            using var outbox = new OutboxEventBus(inner, store);

            var sub = outbox.Subscribe(handler);

            sub.IsActive.Should().BeTrue();
            sub.EventType.Should().Be(typeof(OrderPlaced));
        }
    }
}
