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
            // SH-M118 (decided by the user 2026-10-18: at-least-once): when a handler throws, the reservation is
            // released, so a retry or a transport redelivery runs again. It was kept (at-most-once), so on the
            // distributed bus — whose callback rethrows precisely so the transport redelivers — the redelivery hit
            // the mark and every handler failure became a lost event.
            if (!await _store.TryMarkProcessedAsync(@event.EventGuid, cancellationToken).ConfigureAwait(false))
            {
                return; // Duplicate — skip
            }

            try
            {
                await next().ConfigureAwait(false);
            }
            catch
            {
                await _store.ReleaseAsync(@event.EventGuid, CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }
    }
}
