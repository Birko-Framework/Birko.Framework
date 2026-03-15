using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.EventBus.Outbox
{
    /// <summary>
    /// Persistence store for outbox entries.
    /// Implement for your database (SQL, MongoDB, etc.) to enable transactional outbox.
    /// </summary>
    public interface IOutboxStore
    {
        /// <summary>
        /// Saves a new entry to the outbox.
        /// Should be called within the same transaction as the business data write.
        /// </summary>
        Task SaveAsync(OutboxEntry entry, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets a batch of pending entries for processing, ordered by CreatedAt.
        /// </summary>
        /// <param name="batchSize">Maximum number of entries to return.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task<IReadOnlyList<OutboxEntry>> GetPendingAsync(int batchSize, CancellationToken cancellationToken = default);

        /// <summary>
        /// Marks an entry as successfully published.
        /// </summary>
        Task MarkPublishedAsync(Guid entryId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Marks an entry as failed, incrementing attempts and recording the error.
        /// </summary>
        Task MarkFailedAsync(Guid entryId, string error, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes entries older than the given cutoff date that are Published or Failed.
        /// </summary>
        Task CleanupAsync(DateTime cutoffDate, CancellationToken cancellationToken = default);
    }
}
