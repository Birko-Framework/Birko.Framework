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
        /// Claims a batch of pending entries for processing, ordered by CreatedAt, and returns them each
        /// carrying the <see cref="OutboxEntry.ClaimToken"/> they were claimed under. An entry held in
        /// <c>Publishing</c> past the store's claim timeout is reclaimed, and that counts as an attempt:
        /// at <paramref name="maxAttempts"/> it becomes <c>Failed</c> instead of being handed out again.
        /// </summary>
        /// <param name="batchSize">Maximum number of entries to return.</param>
        /// <param name="maxAttempts">The attempt cap, from <c>OutboxOptions.MaxAttempts</c> — the same one
        /// <see cref="MarkFailedAsync"/> is given, so a reclaim and a failure count against one budget (TASK-710).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task<IReadOnlyList<OutboxEntry>> GetPendingAsync(int batchSize, int maxAttempts, CancellationToken cancellationToken = default);

        /// <summary>
        /// Marks a claimed entry as successfully published. Applies only while the claim
        /// <paramref name="entry"/> was handed out under is still current; once the entry was reclaimed
        /// by another processor, the mark is that processor's to make and this call changes nothing
        /// (TASK-710). Throws <see cref="ArgumentException"/> for an entry that carries no claim.
        /// </summary>
        Task MarkPublishedAsync(OutboxEntry entry, CancellationToken cancellationToken = default);

        /// <summary>
        /// Marks an entry as failed, incrementing attempts and recording the error. Once the entry's
        /// attempt count reaches <paramref name="maxAttempts"/> the store must transition it to
        /// <c>Failed</c> (stop retrying); otherwise it stays <c>Pending</c> for another attempt. The
        /// cap is passed in (from <c>OutboxOptions.MaxAttempts</c>) rather than hardcoded per store
        /// (CR-H115). Like <see cref="MarkPublishedAsync"/>, it applies only while the entry's claim is current.
        /// </summary>
        Task MarkFailedAsync(OutboxEntry entry, string error, int maxAttempts, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes entries older than the given cutoff date that are Published or Failed.
        /// </summary>
        Task CleanupAsync(DateTime cutoffDate, CancellationToken cancellationToken = default);
    }
}
