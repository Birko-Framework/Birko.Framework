using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DomainEvent = Birko.Data.EventSourcing.Events.IEvent;
using Birko.Data.EventSourcing.Events;

namespace Birko.EventBus.EventSourcing
{
    /// <summary>
    /// Decorator for <see cref="IAsyncEventStore"/> that publishes appended events
    /// to an <see cref="Birko.EventBus.IEventBus"/> after they are persisted.
    /// Bridges the event sourcing data layer with the event bus messaging layer.
    /// </summary>
    public class EventStoreEventBus : IAsyncEventStore
    {
        private readonly IAsyncEventStore _inner;
        private readonly Birko.EventBus.IEventBus _eventBus;

        /// <summary>
        /// Creates a new event store that publishes events to the bus after appending.
        /// </summary>
        /// <param name="inner">The underlying async event store.</param>
        /// <param name="eventBus">The event bus to publish to.</param>
        public EventStoreEventBus(IAsyncEventStore inner, Birko.EventBus.IEventBus eventBus)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        }

        public async Task AppendAsync(DomainEvent @event, CancellationToken cancellationToken = default)
        {
            await _inner.AppendAsync(@event, cancellationToken).ConfigureAwait(false);
            await PublishDomainEventAsync(@event, cancellationToken).ConfigureAwait(false);
        }

        public async Task AppendRangeAsync(IEnumerable<DomainEvent> events, CancellationToken cancellationToken = default)
        {
            var eventList = events is IList<DomainEvent> list ? list : new List<DomainEvent>(events);
            await _inner.AppendRangeAsync(eventList, cancellationToken).ConfigureAwait(false);

            foreach (var @event in eventList)
            {
                await PublishDomainEventAsync(@event, cancellationToken).ConfigureAwait(false);
            }
        }

        public Task<IEnumerable<DomainEvent>> ReadAsync(Guid aggregateId, CancellationToken cancellationToken = default)
        {
            return _inner.ReadAsync(aggregateId, cancellationToken);
        }

        public Task<IEnumerable<DomainEvent>> ReadUpToVersionAsync(Guid aggregateId, long maxVersion, CancellationToken cancellationToken = default)
        {
            return _inner.ReadUpToVersionAsync(aggregateId, maxVersion, cancellationToken);
        }

        public Task<IEnumerable<DomainEvent>> ReadFromVersionAsync(Guid aggregateId, long fromVersion, CancellationToken cancellationToken = default)
        {
            return _inner.ReadFromVersionAsync(aggregateId, fromVersion, cancellationToken);
        }

        public Task<long> GetVersionAsync(Guid aggregateId, CancellationToken cancellationToken = default)
        {
            return _inner.GetVersionAsync(aggregateId, cancellationToken);
        }

        public Task<IEnumerable<DomainEvent>> ReadAllFromAsync(DateTime from, CancellationToken cancellationToken = default)
        {
            return _inner.ReadAllFromAsync(from, cancellationToken);
        }

        private Task PublishDomainEventAsync(DomainEvent @event, CancellationToken cancellationToken)
        {
            var domainEventPublished = new DomainEventPublished(@event);
            return _eventBus.PublishAsync(domainEventPublished, cancellationToken);
        }
    }
}
