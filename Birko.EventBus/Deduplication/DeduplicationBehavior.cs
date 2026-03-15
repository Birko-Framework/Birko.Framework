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
            if (await _store.ExistsAsync(@event.EventId, cancellationToken).ConfigureAwait(false))
            {
                return; // Duplicate — skip
            }

            await next().ConfigureAwait(false);
            await _store.MarkProcessedAsync(@event.EventId, cancellationToken).ConfigureAwait(false);
        }
    }
}
