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
    }
}
