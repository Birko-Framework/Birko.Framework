using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Time;

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
        private readonly IDateTimeProvider _clock;
        private readonly int _maxAttempts;

        /// <summary>
        /// Creates the store. <paramref name="staleClaimTimeout"/> is how long an entry may stay in
        /// <see cref="OutboxStatus.Publishing"/> before a subsequent <see cref="GetPendingAsync"/>
        /// reclaims it (recovery from a crashed processor). Defaults to 5 minutes.
        /// </summary>
        /// <param name="staleClaimTimeout">How long a claim may be held before it is reclaimed.</param>
        /// <param name="clock">Stamps ClaimedAt / PublishedAt and judges staleness (SH-L123: it used DateTime.UtcNow,
        /// while the processor's retention cutoff used its injected clock). Defaults to the system clock.</param>
        /// <param name="maxAttempts">The attempt cap for <see cref="GetPendingAsync(int, CancellationToken)"/> only; the
        /// processor passes <c>OutboxOptions.MaxAttempts</c> (TASK-710).</param>
        public InMemoryOutboxStore(TimeSpan? staleClaimTimeout = null, IDateTimeProvider? clock = null, int maxAttempts = 5)
        {
            _staleClaimTimeout = staleClaimTimeout ?? TimeSpan.FromMinutes(5);
            _clock = clock ?? new SystemDateTimeProvider();
            _maxAttempts = maxAttempts;
        }

        public Task SaveAsync(OutboxEntry entry, CancellationToken cancellationToken = default)
        {
            _entries[entry.Guid] = entry;
            return Task.CompletedTask;
        }

        /// <summary>Claims a batch with the attempt cap this store was constructed with.</summary>
        public Task<IReadOnlyList<OutboxEntry>> GetPendingAsync(int batchSize, CancellationToken cancellationToken = default)
            => GetPendingAsync(batchSize, _maxAttempts, cancellationToken);

        public Task<IReadOnlyList<OutboxEntry>> GetPendingAsync(int batchSize, int maxAttempts, CancellationToken cancellationToken = default)
        {
            // Atomically CLAIM the batch: flip Pending -> Publishing under a lock so two concurrent
            // processors never return (and then publish) the same entries — the duplicate-publish gap
            // OutboxStatus.Publishing was meant to close but never did (CR-H116). Stale claims (a
            // processor that crashed mid-publish) are reclaimed after _staleClaimTimeout.
            lock (_claimLock)
            {
                var now = _clock.UtcNow;

                foreach (var stale in _entries.Values.Where(e =>
                    e.Status == OutboxStatus.Publishing &&
                    (e.ClaimedAt == null || now - e.ClaimedAt.Value >= _staleClaimTimeout)))
                {
                    // TASK-710: a reclaim counts as an attempt, against the cap the failures count against, as in
                    // SqlOutboxStore. It was uncounted here, so an entry that killed its processor came round forever.
                    stale.Attempts++;
                    stale.ClaimedAt = null;
                    stale.ClaimToken = null;
                    if (stale.Attempts >= maxAttempts)
                    {
                        stale.Status = OutboxStatus.Failed;
                        stale.LastError = $"Abandoned: held in Publishing for longer than {_staleClaimTimeout} "
                            + $"after {stale.Attempts} attempt(s), and the attempt budget is spent.";
                    }
                    else
                    {
                        stale.Status = OutboxStatus.Pending;
                    }
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
                    entry.ClaimToken = Guid.NewGuid();
                }

                // TASK-710: copies, so the token the processor holds is the one it was handed. The stored objects were
                // handed out, and a reclaim rewrote the claim under the slow processor's feet.
                return Task.FromResult<IReadOnlyList<OutboxEntry>>(claimed.Select(Copy).ToList());
            }
        }

        public Task MarkPublishedAsync(OutboxEntry claimed, CancellationToken cancellationToken = default)
        {
            var (entryId, token) = Claim(claimed);
            // SH-L122: under the claim lock, as every touch of an entry's state is.
            // SH-L121 (waived): an unknown id stays a no-op. SqlOutboxStore does the same, IOutboxStore declares no
            // result, and CleanupAsync removes only Published / Failed entries, so a claimed (Publishing) entry cannot
            // vanish under the processor between its claim and its mark.
            lock (_claimLock)
            {
                // TASK-710: only while the claim it was handed out under is current.
                if (_entries.TryGetValue(entryId, out var entry) && Holds(entry, token))
                {
                    entry.Status = OutboxStatus.Published;
                    entry.PublishedAt = _clock.UtcNow;
                    entry.ClaimedAt = null;
                    entry.ClaimToken = null;
                }
            }

            return Task.CompletedTask;
        }

        public Task MarkFailedAsync(OutboxEntry claimed, string error, int maxAttempts, CancellationToken cancellationToken = default)
        {
            var (entryId, token) = Claim(claimed);
            lock (_claimLock)
            {
                if (_entries.TryGetValue(entryId, out var entry) && Holds(entry, token))
                {
                    entry.Attempts++;
                    entry.LastError = error;
                    entry.ClaimedAt = null;
                    entry.ClaimToken = null;
                    // Honor the configured cap instead of a hardcoded 5 (CR-H115).
                    entry.Status = entry.Attempts >= maxAttempts ? OutboxStatus.Failed : OutboxStatus.Pending;
                }
            }

            return Task.CompletedTask;
        }

        public Task CleanupAsync(DateTime cutoffDate, CancellationToken cancellationToken = default)
        {
            lock (_claimLock)
            {
                var toRemove = _entries.Values
                    .Where(e => (e.Status == OutboxStatus.Published || e.Status == OutboxStatus.Failed)
                        && e.CreatedAt < cutoffDate)
                    .Select(e => e.Guid)
                    .ToList();

                foreach (var id in toRemove)
                {
                    _entries.TryRemove(id, out _);
                }
            }

            return Task.CompletedTask;
        }

        private static bool Holds(OutboxEntry stored, Guid token)
            => stored.Status == OutboxStatus.Publishing && stored.ClaimToken == token;

        private static (Guid Id, Guid Token) Claim(OutboxEntry entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (entry.ClaimToken is not Guid token)
                throw new ArgumentException("The entry carries no claim token: only an entry GetPendingAsync handed out can be marked.", nameof(entry));
            return (entry.Guid, token);
        }

        private static OutboxEntry Copy(OutboxEntry e) => new()
        {
            Guid = e.Guid,
            EventGuid = e.EventGuid,
            EventType = e.EventType,
            Payload = e.Payload,
            Source = e.Source,
            CorrelationGuid = e.CorrelationGuid,
            TenantGuid = e.TenantGuid,
            Headers = new Dictionary<string, string>(e.Headers),
            Status = e.Status,
            CreatedAt = e.CreatedAt,
            PublishedAt = e.PublishedAt,
            ClaimedAt = e.ClaimedAt,
            ClaimToken = e.ClaimToken,
            Attempts = e.Attempts,
            LastError = e.LastError,
        };

        /// <summary>
        /// Gets all entries (for testing).
        /// </summary>
        public IReadOnlyList<OutboxEntry> GetAll()
        {
            lock (_claimLock)
            {
                return _entries.Values.ToList();
            }
        }
    }
}
