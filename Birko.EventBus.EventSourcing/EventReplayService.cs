using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.EventSourcing.Events;

namespace Birko.EventBus.EventSourcing
{
    /// <summary>
    /// Replays events from an event store through the event bus.
    /// Useful for rebuilding projections/read models or reprocessing historical events.
    /// </summary>
    public class EventReplayService
    {
        private readonly IAsyncEventStore _eventStore;
        private readonly Birko.EventBus.IEventBus _eventBus;

        public EventReplayService(IAsyncEventStore eventStore, Birko.EventBus.IEventBus eventBus)
        {
            _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        }

        /// <summary>
        /// Replays all events for an aggregate through the event bus.
        /// </summary>
        public async Task<int> ReplayAggregateAsync(Guid aggregateId, CancellationToken cancellationToken = default)
        {
            var events = await _eventStore.ReadAsync(aggregateId, cancellationToken).ConfigureAwait(false);
            var count = 0;

            foreach (var @event in events)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _eventBus.PublishAsync(new DomainEventPublished(@event), cancellationToken).ConfigureAwait(false);
                count++;
            }

            return count;
        }

        /// <summary>
        /// Replays events for an aggregate from a specific version onwards.
        /// </summary>
        public async Task<int> ReplayFromVersionAsync(Guid aggregateId, long fromVersion, CancellationToken cancellationToken = default)
        {
            var events = await _eventStore.ReadFromVersionAsync(aggregateId, fromVersion, cancellationToken).ConfigureAwait(false);
            var count = 0;

            foreach (var @event in events)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _eventBus.PublishAsync(new DomainEventPublished(@event), cancellationToken).ConfigureAwait(false);
                count++;
            }

            return count;
        }

        /// <summary>
        /// Replays all events from a given timestamp through the event bus.
        /// Useful for rebuilding projections from a known point in time.
        /// </summary>
        public async Task<int> ReplayAllFromAsync(DateTime from, CancellationToken cancellationToken = default)
        {
            var events = await _eventStore.ReadAllFromAsync(from, cancellationToken).ConfigureAwait(false);
            var count = 0;

            foreach (var @event in events)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _eventBus.PublishAsync(new DomainEventPublished(@event), cancellationToken).ConfigureAwait(false);
                count++;
            }

            return count;
        }
    }
}
