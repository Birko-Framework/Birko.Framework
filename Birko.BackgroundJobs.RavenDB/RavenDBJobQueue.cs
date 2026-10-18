using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.BackgroundJobs.RavenDB.Models;
using Birko.Data.RavenDB.Stores;
using Birko.Data.Stores;
using Birko.Configuration;

namespace Birko.BackgroundJobs.RavenDB
{
    /// <summary>
    /// RavenDB-based persistent job queue using Birko.Data.RavenDB stores.
    /// Jobs are stored as documents in a RavenDB database.
    /// </summary>
    public class RavenDBJobQueue : IJobQueue
    {
        private const int MaxClaimAttempts = 32;

        private readonly AsyncRavenDBStore<RavenJobDescriptorModel> _store;
        private readonly RetryPolicy _retryPolicy;

        /// <summary>
        /// Creates a new RavenDB job queue.
        /// </summary>
        public RavenDBJobQueue(Birko.Data.RavenDB.Stores.Settings settings, RetryPolicy? retryPolicy = null)
        {
            _store = new AsyncRavenDBStore<RavenJobDescriptorModel>();
            _store.SetSettings(settings);
            _retryPolicy = retryPolicy ?? RetryPolicy.Default;
        }

        /// <summary>
        /// Creates a new RavenDB job queue from an existing store.
        /// </summary>
        public RavenDBJobQueue(AsyncRavenDBStore<RavenJobDescriptorModel> store, RetryPolicy? retryPolicy = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _retryPolicy = retryPolicy ?? RetryPolicy.Default;
        }

        /// <summary>
        /// Gets the underlying store for advanced scenarios (e.g., transaction context).
        /// </summary>
        public AsyncRavenDBStore<RavenJobDescriptorModel> Store => _store;

        public async Task<Guid> EnqueueAsync(JobDescriptor descriptor, CancellationToken cancellationToken = default)
        {
            var model = RavenJobDescriptorModel.FromDescriptor(descriptor);
            var id = await _store.CreateAsync(model, ct: cancellationToken).ConfigureAwait(false);
            return id;
        }

        public async Task<JobDescriptor?> DequeueAsync(string? queueName = null, CancellationToken cancellationToken = default)
        {
            var pendingStatus = (int)JobStatus.Pending;
            var scheduledStatus = (int)JobStatus.Scheduled;
            var processingStatus = (int)JobStatus.Processing;

            for (int attempt = 0; attempt < MaxClaimAttempts; attempt++)
            {
                var now = DateTime.UtcNow;

                IEnumerable<RavenJobDescriptorModel> candidates;
                if (queueName != null)
                {
                    candidates = await _store.ReadAsync(
                        filter: j => (j.Status == pendingStatus || (j.Status == scheduledStatus && j.ScheduledAt != null && j.ScheduledAt <= now))
                                  && (j.QueueName == null || j.QueueName == queueName),
                        orderBy: OrderBy<RavenJobDescriptorModel>.ByDescending(j => j.Priority).ThenBy(j => j.EnqueuedAt),
                        limit: 1,
                        ct: cancellationToken
                    ).ConfigureAwait(false);
                }
                else
                {
                    candidates = await _store.ReadAsync(
                        filter: j => j.Status == pendingStatus || (j.Status == scheduledStatus && j.ScheduledAt != null && j.ScheduledAt <= now),
                        orderBy: OrderBy<RavenJobDescriptorModel>.ByDescending(j => j.Priority).ThenBy(j => j.EnqueuedAt),
                        limit: 1,
                        ct: cancellationToken
                    ).ConfigureAwait(false);
                }

                var candidate = candidates.FirstOrDefault();
                if (candidate == null)
                {
                    return null;
                }

                // Conditional claim guarded on the still-eligible status + re-read verify via ClaimToken.
                // The verify resolves a single winner even under RavenDB last-write-wins: whichever worker
                // wrote last owns the token, and every worker re-reads the same final token, so exactly one
                // matches. Job handlers should still be idempotent (documented in the project CLAUDE.md).
                var claimId = candidate.Guid;
                var originalStatus = candidate.Status;
                var claimToken = Guid.NewGuid();
                var seenAttempts = candidate.AttemptCount;

                await _store.UpdateAsync(
                    filter: // TASK-158 (SH-M012): Status alone is ABA — a job another worker claimed, ran and failed back to Scheduled
                    // matched again, and the stale claim ran it inside its backoff with a lower AttemptCount.
                    j => j.Guid == claimId && j.Status == originalStatus && j.AttemptCount == seenAttempts,
                    updates: new PropertyUpdate<RavenJobDescriptorModel>()
                        .Set(j => j.Status, processingStatus)
                        .Set(j => j.ClaimToken, claimToken)
                        .Set(j => j.AttemptCount, candidate.AttemptCount + 1)
                        .Set(j => j.LastAttemptAt, now),
                    ct: cancellationToken
                ).ConfigureAwait(false);

                var claimed = await _store.ReadAsync(j => j.Guid == claimId, cancellationToken).ConfigureAwait(false);
                if (claimed != null && claimed.ClaimToken == claimToken)
                {
                    return claimed.ToDescriptor();
                }
            }

            return null;
        }

        // TASK-158 (SH-M013): each transition is one guarded native update of the fields it changes. They read the whole
        // row and wrote it back, so a stale reporter overwrote a newer claim's Status, AttemptCount and ClaimToken, and a
        // cancel read Pending and then overwrote a claim that landed in between. A full ownership check would need the
        // claim token on CompleteAsync / FailAsync (an IJobQueue change); the guards close what the API can express.
        public async Task CompleteAsync(Guid jobId, CancellationToken cancellationToken = default)
        {
            var processingStatus = (int)JobStatus.Processing;
            await _store.UpdateAsync(
                filter: j => j.Guid == jobId && j.Status == processingStatus,
                updates: new PropertyUpdate<RavenJobDescriptorModel>()
                    .Set(j => j.Status, (int)JobStatus.Completed)
                    .Set(j => j.CompletedAt, DateTime.UtcNow),
                ct: cancellationToken
            ).ConfigureAwait(false);
        }

        public async Task FailAsync(Guid jobId, string error, CancellationToken cancellationToken = default)
        {
            var processingStatus = (int)JobStatus.Processing;
            var model = await _store.ReadAsync(j => j.Guid == jobId && j.Status == processingStatus, cancellationToken).ConfigureAwait(false);
            if (model == null) return;

            var attempts = model.AttemptCount;
            var maxRetries = model.MaxRetries > 0 ? model.MaxRetries : _retryPolicy.MaxRetries;
            var now = DateTime.UtcNow;
            var update = new PropertyUpdate<RavenJobDescriptorModel>().Set(j => j.LastError, error);
            if (attempts < maxRetries)
                update = update.Set(j => j.ScheduledAt, now.Add(_retryPolicy.GetDelay(attempts))).Set(j => j.Status, (int)JobStatus.Scheduled);
            else
                update = update.Set(j => j.CompletedAt, now).Set(j => j.Status, (int)JobStatus.Dead);

            await _store.UpdateAsync(
                filter: j => j.Guid == jobId && j.Status == processingStatus && j.AttemptCount == attempts,
                updates: update,
                ct: cancellationToken
            ).ConfigureAwait(false);
        }

        public async Task<bool> CancelAsync(Guid jobId, CancellationToken cancellationToken = default)
        {
            var pendingStatus = (int)JobStatus.Pending;
            var scheduledStatus = (int)JobStatus.Scheduled;
            var cancelledStatus = (int)JobStatus.Cancelled;
            // A stamp only this call writes: the store reports no affected-row count, so success is read back. Whole
            // milliseconds, which every backend stores exactly (MongoDB and Elasticsearch keep nothing finer).
            var nowMs = DateTime.UtcNow;
            var stamp = new DateTime(nowMs.Ticks - nowMs.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc).AddMilliseconds(System.Random.Shared.Next(0, 1000));

            await _store.UpdateAsync(
                filter: j => j.Guid == jobId && (j.Status == pendingStatus || j.Status == scheduledStatus),
                updates: new PropertyUpdate<RavenJobDescriptorModel>()
                    .Set(j => j.Status, cancelledStatus)
                    .Set(j => j.CompletedAt, stamp),
                ct: cancellationToken
            ).ConfigureAwait(false);

            var after = await _store.ReadAsync(j => j.Guid == jobId, cancellationToken).ConfigureAwait(false);
            return after != null && after.Status == cancelledStatus && after.CompletedAt == stamp;
        }

        public async Task<JobDescriptor?> GetAsync(Guid jobId, CancellationToken cancellationToken = default)
        {
            var model = await _store.ReadAsync(j => j.Guid == jobId, cancellationToken).ConfigureAwait(false);
            return model?.ToDescriptor();
        }

        public async Task<IReadOnlyList<JobDescriptor>> GetByStatusAsync(JobStatus status, int limit = 100, CancellationToken cancellationToken = default)
        {
            var statusInt = (int)status;

            var models = await _store.ReadAsync(
                filter: j => j.Status == statusInt,
                orderBy: OrderBy<RavenJobDescriptorModel>.ByDescending(j => j.EnqueuedAt),
                limit: limit,
                ct: cancellationToken
            ).ConfigureAwait(false);

            return models.Select(m => m.ToDescriptor()).ToList();
        }

        public async Task<int> PurgeAsync(TimeSpan olderThan, CancellationToken cancellationToken = default)
        {
            var cutoff = DateTime.UtcNow.Subtract(olderThan);
            var completedStatus = (int)JobStatus.Completed;
            var deadStatus = (int)JobStatus.Dead;
            var cancelledStatus = (int)JobStatus.Cancelled;

            var toPurge = await _store.ReadAsync(
                filter: j => (j.Status == completedStatus || j.Status == deadStatus || j.Status == cancelledStatus)
                          && j.CompletedAt != null && j.CompletedAt < cutoff,
                ct: cancellationToken
            ).ConfigureAwait(false);

            var list = toPurge.ToList();
            if (list.Count > 0)
            {
                await _store.DeleteAsync(list, cancellationToken).ConfigureAwait(false);
            }

            return list.Count;
        }

    }
}
