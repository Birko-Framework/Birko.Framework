using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.EventBus.Pipeline;

namespace Birko.EventBus.Deduplication
{
    /// <summary>
    /// Pipeline behavior that skips duplicate events based on EventId.
    /// Events already seen by the <see cref="IDeduplicationStore"/> are silently dropped.
    /// </summary>
    public class DeduplicationBehavior : IEventPipelineBehavior
    {
        private readonly IDeduplicationStore _store;

        public DeduplicationBehavior(IDeduplicationStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public async Task HandleAsync(IEvent @event, EventContext context, Func<Task> next, CancellationToken cancellationToken = default)
        {
            // CR-L248: atomically reserve the EventId BEFORE running handlers. TryMarkProcessedAsync
            // returns false if the event was already seen — or was claimed concurrently by another
            // publish — so duplicates, including a simultaneous double-publish, are dropped exactly once
            // (the old Exists-then-Mark pair let two concurrent publishes both run the handlers).
            // This is mark-before = at-most-once: a handler that throws does NOT un-mark the event, so
            // it will not be reprocessed on a later republish. A caller needing at-least-once semantics
            // (reprocess on handler failure) should mark only after successful handling with a different
            // behavior rather than this dedup guard.
            if (!await _store.TryMarkProcessedAsync(@event.EventId, cancellationToken).ConfigureAwait(false))
            {
                return; // Duplicate — skip
            }

            await next().ConfigureAwait(false);
        }
    }
}
