using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Birko.EventBus.Enrichment;
using Birko.MessageQueue.Serialization;

namespace Birko.EventBus.Outbox.Publishing
{
    /// <summary>
    /// IEventBus decorator that writes events to the outbox store instead of publishing directly.
    /// The event is persisted in the same transaction as the business data (when using a shared DB connection).
    /// The <see cref="OutboxProcessor"/> picks up pending entries and publishes them via the inner event bus.
    /// </summary>
    public class OutboxEventBus : IEventBus
    {
        private readonly IEventBus _inner;
        private readonly IOutboxStore _store;
        private readonly IMessageSerializer _serializer;
        private readonly IReadOnlyList<IEventEnricher> _enrichers;
        private bool _disposed;

        /// <summary>
        /// Creates a new outbox event bus decorator.
        /// </summary>
        /// <param name="inner">The inner event bus (e.g., DistributedEventBus) used for actual publishing by the processor.</param>
        /// <param name="store">The outbox persistence store.</param>
        /// <param name="serializer">Serializer for event payloads. Defaults to JsonMessageSerializer.</param>
        /// <param name="enrichers">Event enrichers to run before persisting.</param>
        public OutboxEventBus(
            IEventBus inner,
            IOutboxStore store,
            IMessageSerializer? serializer = null,
            IEnumerable<IEventEnricher>? enrichers = null)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _serializer = serializer ?? new JsonMessageSerializer();
            _enrichers = enrichers != null ? new List<IEventEnricher>(enrichers) : [];
        }

        /// <summary>
        /// Writes the event to the outbox store instead of publishing directly.
        /// The OutboxProcessor will publish it later.
        /// </summary>
        public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : IEvent
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var context = EventContext.From(@event);

            // Run enrichers to populate context (correlation, tenant, etc.)
            foreach (var enricher in _enrichers)
            {
                await enricher.EnrichAsync(@event, context, cancellationToken).ConfigureAwait(false);
            }

            var entry = new OutboxEntry
            {
                EventId = @event.EventId,
                EventType = @event.GetType().AssemblyQualifiedName!,
                Payload = _serializer.Serialize(@event),
                Source = @event.Source,
                CorrelationId = context.CorrelationId,
                TenantId = context.TenantId,
                Headers = new Dictionary<string, string>(context.Metadata)
            };

            await _store.SaveAsync(entry, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Delegates subscription to the inner event bus.
        /// </summary>
        public IEventSubscription Subscribe<TEvent>(IEventHandler<TEvent> handler) where TEvent : IEvent
        {
            return _inner.Subscribe(handler);
        }

        /// <summary>
        /// Gets the inner event bus (used by OutboxProcessor for actual publishing).
        /// </summary>
        internal IEventBus Inner => _inner;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _inner.Dispose();
        }
    }
}
