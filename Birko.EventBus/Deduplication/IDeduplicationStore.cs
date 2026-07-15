using System;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.EventBus.Deduplication
{
    /// <summary>
    /// Tracks processed event IDs for deduplication.
    /// </summary>
    public interface IDeduplicationStore
    {
        /// <summary>
        /// Checks if the event has already been processed.
        /// </summary>
        /// <param name="eventId">The event ID to check.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if the event was already processed.</returns>
        Task<bool> ExistsAsync(Guid eventId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Records the event as processed.
        /// </summary>
        /// <param name="eventId">The event ID to record.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task MarkProcessedAsync(Guid eventId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Atomically records the event as processed <em>if it was not already present</em>, returning
        /// <c>true</c> when this call performed the mark (the event had not been seen) and <c>false</c>
        /// when it was already recorded (a duplicate, including one claimed concurrently by another caller).
        /// </summary>
        /// <remarks>
        /// CR-L248: this is the race-free primitive the deduplication pipeline needs — a separate
        /// <see cref="ExistsAsync"/> then <see cref="MarkProcessedAsync"/> lets two concurrent publishes
        /// both observe "not seen" and both proceed. Implementations backed by a concurrent primitive
        /// (e.g. an atomic add / SETNX) should override this. The default implementation composes
        /// Exists + Mark and is therefore <b>not</b> atomic across concurrent callers — provided only for
        /// source compatibility with existing implementers.
        /// </remarks>
        /// <param name="eventId">The event ID to reserve.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if this call marked the event; false if it was already processed.</returns>
        async Task<bool> TryMarkProcessedAsync(Guid eventId, CancellationToken cancellationToken = default)
        {
            if (await ExistsAsync(eventId, cancellationToken).ConfigureAwait(false))
            {
                return false;
            }
            await MarkProcessedAsync(eventId, cancellationToken).ConfigureAwait(false);
            return true;
        }
    }
}
