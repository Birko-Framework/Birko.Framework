using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Birko.Data.EventSourcing.Events;
using Birko.EventBus.EventSourcing;
using Birko.EventBus.Local;
using FluentAssertions;
using Xunit;

namespace Birko.EventBus.Tests.EventSourcing
{
    public class EventStoreEventBusTests
    {
        [Fact]
        public async Task AppendAsync_PublishesDomainEventPublished()
        {
            var store = new TestAsyncEventStore();
            var handler = new DomainEventHandler();
            using var bus = new InProcessEventBus();
            bus.Subscribe(handler);

            var decorator = new EventStoreEventBus(store, bus);
            var domainEvent = new DomainEvent(Guid.NewGuid(), 1, "Created", "{\"name\":\"test\"}");

            await decorator.AppendAsync(domainEvent);

            handler.ReceivedEvents.Should().ContainSingle();
            handler.ReceivedEvents[0].AggregateId.Should().Be(domainEvent.AggregateId);
            handler.ReceivedEvents[0].Version.Should().Be(1);
            handler.ReceivedEvents[0].DomainEventType.Should().Be("Created");
            handler.ReceivedEvents[0].EventData.Should().Be("{\"name\":\"test\"}");
        }

        [Fact]
        public async Task AppendAsync_PersistsToInnerStore()
        {
            var store = new TestAsyncEventStore();
            using var bus = new InProcessEventBus();
            var decorator = new EventStoreEventBus(store, bus);

            var aggregateId = Guid.NewGuid();
            var domainEvent = new DomainEvent(aggregateId, 1, "Created", "{}");

            await decorator.AppendAsync(domainEvent);

            var events = await store.ReadAsync(aggregateId);
            events.Should().ContainSingle();
        }

        [Fact]
        public async Task AppendRangeAsync_PublishesAllEvents()
        {
            var store = new TestAsyncEventStore();
            var handler = new DomainEventHandler();
            using var bus = new InProcessEventBus();
            bus.Subscribe(handler);

            var decorator = new EventStoreEventBus(store, bus);
            var aggregateId = Guid.NewGuid();

            await decorator.AppendRangeAsync(new[]
            {
                new DomainEvent(aggregateId, 1, "Created", "{}"),
                new DomainEvent(aggregateId, 2, "Updated", "{\"name\":\"new\"}")
            });

            handler.ReceivedEvents.Should().HaveCount(2);
            handler.ReceivedEvents[0].DomainEventType.Should().Be("Created");
            handler.ReceivedEvents[1].DomainEventType.Should().Be("Updated");
        }

        [Fact]
        public async Task ReadAsync_DelegatesToInnerStore()
        {
            var store = new TestAsyncEventStore();
            using var bus = new InProcessEventBus();
            var decorator = new EventStoreEventBus(store, bus);

            var aggregateId = Guid.NewGuid();
            await store.AppendAsync(new DomainEvent(aggregateId, 1, "Created", "{}"));

            var events = await decorator.ReadAsync(aggregateId);
            events.Should().ContainSingle();
        }

        [Fact]
        public async Task GetVersionAsync_DelegatesToInnerStore()
        {
            var store = new TestAsyncEventStore();
            using var bus = new InProcessEventBus();
            var decorator = new EventStoreEventBus(store, bus);

            var aggregateId = Guid.NewGuid();
            await store.AppendAsync(new DomainEvent(aggregateId, 3, "Updated", "{}"));

            var version = await decorator.GetVersionAsync(aggregateId);
            version.Should().Be(3);
        }

        private class DomainEventHandler : IEventHandler<DomainEventPublished>
        {
            public List<DomainEventPublished> ReceivedEvents { get; } = [];

            public Task HandleAsync(DomainEventPublished @event, EventContext context, System.Threading.CancellationToken cancellationToken = default)
            {
                ReceivedEvents.Add(@event);
                return Task.CompletedTask;
            }
        }
    }
}
