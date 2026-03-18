using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Birko.Time;

namespace Birko.EventBus.Deduplication
{
    /// <summary>
    /// In-memory deduplication store using ConcurrentDictionary with TTL expiry.
    /// Suitable for single-process scenarios. For distributed, use a persistent store.
    /// </summary>
    public class InMemoryDeduplicationStore : IDeduplicationStore
    {
        private readonly ConcurrentDictionary<Guid, DateTime> _processed = new();
        private readonly TimeSpan _ttl;
        private readonly IDateTimeProvider _clock;
        private DateTime _lastCleanup;
        private readonly TimeSpan _cleanupInterval = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Creates a new in-memory deduplication store.
        /// </summary>
        /// <param name="ttl">How long to remember processed event IDs. Default is 1 hour.</param>
        /// <param name="clock">Optional clock provider. Defaults to SystemDateTimeProvider.</param>
        public InMemoryDeduplicationStore(TimeSpan? ttl = null, IDateTimeProvider? clock = null)
        {
            _ttl = ttl ?? TimeSpan.FromHours(1);
            _clock = clock ?? new SystemDateTimeProvider();
            _lastCleanup = _clock.UtcNow;
        }

        public Task<bool> ExistsAsync(Guid eventId, CancellationToken cancellationToken = default)
        {
            CleanupIfNeeded();
            return Task.FromResult(_processed.ContainsKey(eventId));
        }

        public Task MarkProcessedAsync(Guid eventId, CancellationToken cancellationToken = default)
        {
            _processed[eventId] = _clock.UtcNow;
            return Task.CompletedTask;
        }

        private void CleanupIfNeeded()
        {
            var now = _clock.UtcNow;
            if (now - _lastCleanup < _cleanupInterval)
            {
                return;
            }

            _lastCleanup = now;
            var cutoff = now - _ttl;
            foreach (var kvp in _processed)
            {
                if (kvp.Value < cutoff)
                {
                    _processed.TryRemove(kvp.Key, out _);
                }
            }
        }
    }
}
