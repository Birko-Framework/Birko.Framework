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
        // CR-L249: stored as ticks so the interval check + claim can be done atomically via Interlocked
        // (a plain DateTime field was read-modify-written without synchronization).
        private long _lastCleanupTicks;
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
            _lastCleanupTicks = _clock.UtcNow.Ticks;
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

        /// <summary>
        /// CR-L248: race-free reserve via <see cref="ConcurrentDictionary{TKey,TValue}.TryAdd"/> — only
        /// the first caller for a given event id succeeds, so concurrent duplicates are dropped.
        /// </summary>
        public Task<bool> TryMarkProcessedAsync(Guid eventId, CancellationToken cancellationToken = default)
        {
            CleanupIfNeeded();
            return Task.FromResult(_processed.TryAdd(eventId, _clock.UtcNow));
        }

        private void CleanupIfNeeded()
        {
            var now = _clock.UtcNow;
            var last = Interlocked.Read(ref _lastCleanupTicks);
            if (now.Ticks - last < _cleanupInterval.Ticks)
            {
                return;
            }

            // CR-L249: claim the cleanup slot atomically — only the thread that swaps in `now` runs the
            // sweep this interval, so concurrent callers can't all pass the check and sweep at once.
            if (Interlocked.CompareExchange(ref _lastCleanupTicks, now.Ticks, last) != last)
            {
                return;
            }

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
