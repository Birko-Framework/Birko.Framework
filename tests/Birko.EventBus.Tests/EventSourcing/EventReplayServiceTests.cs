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
    public class EventReplayServiceTests
    {
        [Fact]
        public async Task ReplayAggregateAsync_PublishesAllEvents()
        {
            var store = new TestAsyncEventStore();
            var handler = new DomainEventHandler();
            using var bus = new InProcessEventBus();
            bus.Subscribe(handler);

            var aggregateId = Guid.NewGuid();
            await store.AppendAsync(new DomainEvent(aggregateId, 1, "Created", "{}"));
            await store.AppendAsync(new DomainEvent(aggregateId, 2, "Updated", "{}"));
            await store.AppendAsync(new DomainEvent(aggregateId, 3, "Updated", "{}"));

            var service = new EventReplayService(store, bus);
            var count = await service.ReplayAggregateAsync(aggregateId);

            count.Should().Be(3);
            handler.ReceivedEvents.Should().HaveCount(3);
        }

        [Fact]
        public async Task ReplayAggregateAsync_EmptyAggregate_ReturnsZero()
        {
            var store = new TestAsyncEventStore();
            using var bus = new InProcessEventBus();

            var service = new EventReplayService(store, bus);
            var count = await service.ReplayAggregateAsync(Guid.NewGuid());

            count.Should().Be(0);
        }

        [Fact]
        public async Task ReplayFromVersionAsync_PublishesFromVersion()
        {
            var store = new TestAsyncEventStore();
            var handler = new DomainEventHandler();
            using var bus = new InProcessEventBus();
            bus.Subscribe(handler);

            var aggregateId = Guid.NewGuid();
            await store.AppendAsync(new DomainEvent(aggregateId, 1, "Created", "{}"));
            await store.AppendAsync(new DomainEvent(aggregateId, 2, "Updated", "{}"));
            await store.AppendAsync(new DomainEvent(aggregateId, 3, "Deleted", "{}"));

            var service = new EventReplayService(store, bus);
            var count = await service.ReplayFromVersionAsync(aggregateId, 2);

            count.Should().Be(2);
            handler.ReceivedEvents.Should().HaveCount(2);
            handler.ReceivedEvents[0].Version.Should().Be(2);
            handler.ReceivedEvents[1].Version.Should().Be(3);
        }

        [Fact]
        public async Task ReplayAllFromAsync_PublishesFromTimestamp()
        {
            var store = new TestAsyncEventStore();
            var handler = new DomainEventHandler();
            using var bus = new InProcessEventBus();
            bus.Subscribe(handler);

            var old = new DomainEvent(Guid.NewGuid(), 1, "Created", "{}") { OccurredAt = DateTime.UtcNow.AddHours(-2) };
            var recent = new DomainEvent(Guid.NewGuid(), 1, "Created", "{}") { OccurredAt = DateTime.UtcNow };

            await store.AppendAsync(old);
            await store.AppendAsync(recent);

            var service = new EventReplayService(store, bus);
            var count = await service.ReplayAllFromAsync(DateTime.UtcNow.AddHours(-1));

            count.Should().Be(1);
            handler.ReceivedEvents.Should().ContainSingle();
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
