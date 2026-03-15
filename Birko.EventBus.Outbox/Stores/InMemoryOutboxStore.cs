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

        public Task SaveAsync(OutboxEntry entry, CancellationToken cancellationToken = default)
        {
            _entries[entry.Id] = entry;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<OutboxEntry>> GetPendingAsync(int batchSize, CancellationToken cancellationToken = default)
        {
            var pending = _entries.Values
                .Where(e => e.Status == OutboxStatus.Pending)
                .OrderBy(e => e.CreatedAt)
                .Take(batchSize)
                .ToList();

            return Task.FromResult<IReadOnlyList<OutboxEntry>>(pending);
        }

        public Task MarkPublishedAsync(Guid entryId, CancellationToken cancellationToken = default)
        {
            if (_entries.TryGetValue(entryId, out var entry))
            {
                entry.Status = OutboxStatus.Published;
                entry.PublishedAt = DateTime.UtcNow;
            }

            return Task.CompletedTask;
        }

        public Task MarkFailedAsync(Guid entryId, string error, CancellationToken cancellationToken = default)
        {
            if (_entries.TryGetValue(entryId, out var entry))
            {
                entry.Attempts++;
                entry.LastError = error;
                entry.Status = entry.Attempts >= 5 ? OutboxStatus.Failed : OutboxStatus.Pending;
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
