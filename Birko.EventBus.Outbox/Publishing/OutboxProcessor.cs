using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.MessageQueue.Serialization;

namespace Birko.EventBus.Outbox.Publishing
{
    /// <summary>
    /// Background processor that polls the outbox store for pending entries
    /// and publishes them via the inner event bus.
    /// </summary>
    public class OutboxProcessor
    {
        private readonly IOutboxStore _store;
        private readonly IEventBus _publisher;
        private readonly IMessageSerializer _serializer;
        private readonly OutboxOptions _options;

        /// <summary>
        /// Creates a new outbox processor.
        /// </summary>
        /// <param name="store">The outbox persistence store.</param>
        /// <param name="publisher">The event bus to publish events through (the inner bus, not the OutboxEventBus).</param>
        /// <param name="options">Processor options.</param>
        /// <param name="serializer">Serializer for deserializing event payloads. Defaults to JsonMessageSerializer.</param>
        public OutboxProcessor(
            IOutboxStore store,
            IEventBus publisher,
            OutboxOptions? options = null,
            IMessageSerializer? serializer = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
            _options = options ?? new OutboxOptions();
            _serializer = serializer ?? new JsonMessageSerializer();
        }

        /// <summary>
        /// Processes one batch of pending outbox entries.
        /// Returns the number of entries processed.
        /// </summary>
        public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken = default)
        {
            var entries = await _store.GetPendingAsync(_options.BatchSize, cancellationToken).ConfigureAwait(false);
            if (entries.Count == 0)
            {
                return 0;
            }

            var processed = 0;

            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var eventType = Type.GetType(entry.EventType);
                    if (eventType == null)
                    {
                        await _store.MarkFailedAsync(entry.Id, $"Cannot resolve type: {entry.EventType}", cancellationToken).ConfigureAwait(false);
                        processed++;
                        continue;
                    }

                    var @event = _serializer.Deserialize(entry.Payload, eventType) as IEvent;
                    if (@event == null)
                    {
                        await _store.MarkFailedAsync(entry.Id, $"Cannot deserialize payload for type: {entry.EventType}", cancellationToken).ConfigureAwait(false);
                        processed++;
                        continue;
                    }

                    // Publish via the inner bus (which sends to MessageQueue or dispatches in-process)
                    await PublishEventAsync(@event, cancellationToken).ConfigureAwait(false);
                    await _store.MarkPublishedAsync(entry.Id, cancellationToken).ConfigureAwait(false);
                    processed++;
                }
                catch (Exception ex)
                {
                    await _store.MarkFailedAsync(entry.Id, ex.Message, cancellationToken).ConfigureAwait(false);
                    processed++;
                }
            }

            return processed;
        }

        /// <summary>
        /// Cleans up old published/failed entries based on retention period.
        /// </summary>
        public Task CleanupAsync(CancellationToken cancellationToken = default)
        {
            var cutoff = DateTime.UtcNow - _options.RetentionPeriod;
            return _store.CleanupAsync(cutoff, cancellationToken);
        }

        private Task PublishEventAsync(IEvent @event, CancellationToken cancellationToken)
        {
            // Use reflection to call PublishAsync<TEvent> with the correct type
            var method = typeof(IEventBus)
                .GetMethod(nameof(IEventBus.PublishAsync))!
                .MakeGenericMethod(@event.GetType());

            return (Task)method.Invoke(_publisher, [@event, cancellationToken])!;
        }
    }
}
