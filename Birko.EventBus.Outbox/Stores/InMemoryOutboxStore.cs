using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.EventBus.Outbox.Stores
{
    /// <summary>
    /// In-memory outbox store for testing and development.
    /// Not suitable for production — entries are lost on process restart.
    /// </summary>
    public class InMemoryOutboxStore : IOutboxStore
    {
        private readonly ConcurrentDictionary<Guid, OutboxEntry> _entries = new();
        private readonly object _claimLock = new();
        private readonly TimeSpan _staleClaimTimeout;

        /// <summary>
        /// Creates the store. <paramref name="staleClaimTimeout"/> is how long an entry may stay in
        /// <see cref="OutboxStatus.Publishing"/> before a subsequent <see cref="GetPendingAsync"/>
        /// reclaims it (recovery from a crashed processor). Defaults to 5 minutes.
        /// </summary>
        public InMemoryOutboxStore(TimeSpan? staleClaimTimeout = null)
        {
            _staleClaimTimeout = staleClaimTimeout ?? TimeSpan.FromMinutes(5);
        }

        public Task SaveAsync(OutboxEntry entry, CancellationToken cancellationToken = default)
        {
            _entries[entry.Id] = entry;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<OutboxEntry>> GetPendingAsync(int batchSize, CancellationToken cancellationToken = default)
        {
            // Atomically CLAIM the batch: flip Pending -> Publishing under a lock so two concurrent
            // processors never return (and then publish) the same entries — the duplicate-publish gap
            // OutboxStatus.Publishing was meant to close but never did (CR-H116). Stale claims (a
            // processor that crashed mid-publish) are reclaimed after _staleClaimTimeout.
            lock (_claimLock)
            {
                var now = DateTime.UtcNow;

                foreach (var stale in _entries.Values.Where(e =>
                    e.Status == OutboxStatus.Publishing &&
                    (e.ClaimedAt == null || now - e.ClaimedAt.Value >= _staleClaimTimeout)))
                {
                    stale.Status = OutboxStatus.Pending;
                    stale.ClaimedAt = null;
                }

                var claimed = _entries.Values
                    .Where(e => e.Status == OutboxStatus.Pending)
                    .OrderBy(e => e.CreatedAt)
                    .Take(batchSize)
                    .ToList();

                foreach (var entry in claimed)
                {
                    entry.Status = OutboxStatus.Publishing;
                    entry.ClaimedAt = now;
                }

                return Task.FromResult<IReadOnlyList<OutboxEntry>>(claimed);
            }
        }

        public Task MarkPublishedAsync(Guid entryId, CancellationToken cancellationToken = default)
        {
            if (_entries.TryGetValue(entryId, out var entry))
            {
                entry.Status = OutboxStatus.Published;
                entry.PublishedAt = DateTime.UtcNow;
                entry.ClaimedAt = null;
            }

            return Task.CompletedTask;
        }

        public Task MarkFailedAsync(Guid entryId, string error, int maxAttempts, CancellationToken cancellationToken = default)
        {
            if (_entries.TryGetValue(entryId, out var entry))
            {
                entry.Attempts++;
                entry.LastError = error;
                entry.ClaimedAt = null;
                // Honor the configured cap instead of a hardcoded 5 (CR-H115).
                entry.Status = entry.Attempts >= maxAttempts ? OutboxStatus.Failed : OutboxStatus.Pending;
            }

            return Task.CompletedTask;
        }

        public Task CleanupAsync(DateTime cutoffDate, CancellationToken cancellationToken = default)
        {
            var toRemove = _entries.Values
                .Where(e => (e.Status == OutboxStatus.Published || e.Status == OutboxStatus.Failed)
                    && e.CreatedAt < cutoffDate)
                .Select(e => e.Id)
                .ToList();

            foreach (var id in toRemove)
            {
                _entries.TryRemove(id, out _);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Gets all entries (for testing).
        /// </summary>
        public IReadOnlyList<OutboxEntry> GetAll()
        {
            return _entries.Values.ToList();
        }
    }
}
