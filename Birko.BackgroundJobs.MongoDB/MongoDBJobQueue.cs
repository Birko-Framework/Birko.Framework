using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Birko.BackgroundJobs.MongoDB.Models;
using Birko.Data.MongoDB.Stores;
using Birko.Data.Stores;
using Birko.Configuration;

namespace Birko.BackgroundJobs.MongoDB
{
    /// <summary>
    /// MongoDB-based persistent job queue using Birko.Data.MongoDB stores.
    /// Jobs are stored as documents in a MongoDB collection.
    /// </summary>
    public class MongoDBJobQueue : IJobQueue
    {
        private const int MaxClaimAttempts = 32;

        private readonly AsyncMongoDBStore<MongoJobDescriptorModel> _store;
        private readonly RetryPolicy _retryPolicy;

        /// <summary>How long a job may stay Processing before it is presumed abandoned (TASK-664; see <see cref="JobReclaim"/>).</summary>
        private readonly TimeSpan _claimTimeout;

        /// <summary>
        /// Creates a new MongoDB job queue.
        /// </summary>
        public MongoDBJobQueue(Birko.Data.MongoDB.Stores.Settings settings, RetryPolicy? retryPolicy = null, TimeSpan? claimTimeout = null)
        {
            _store = new AsyncMongoDBStore<MongoJobDescriptorModel>();
            _store.SetSettings(settings);
            _retryPolicy = retryPolicy ?? RetryPolicy.Default;
            _claimTimeout = JobReclaim.Resolve(claimTimeout);
        }

        /// <summary>
        /// Creates a new MongoDB job queue from an existing store.
        /// </summary>
        public MongoDBJobQueue(AsyncMongoDBStore<MongoJobDescriptorModel> store, RetryPolicy? retryPolicy = null, TimeSpan? claimTimeout = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _retryPolicy = retryPolicy ?? RetryPolicy.Default;
            _claimTimeout = JobReclaim.Resolve(claimTimeout);
        }

        /// <summary>
        /// Gets the underlying store for advanced scenarios (e.g., transaction context).
        /// </summary>
        public AsyncMongoDBStore<MongoJobDescriptorModel> Store => _store;

        public async Task<Guid> EnqueueAsync(JobDescriptor descriptor, CancellationToken cancellationToken = default)
        {
            var model = MongoJobDescriptorModel.FromDescriptor(descriptor);
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
                // TASK-664: a job left Processing by a worker that died is offered again once held longer than the
                // claim timeout, as on SQL (TASK-451); before, it was stranded forever.
                var staleBefore = now - _claimTimeout;

                IEnumerable<MongoJobDescriptorModel> candidates;
                if (queueName != null)
                {
                    candidates = await _store.ReadAsync(
                        filter: j => (j.Status == pendingStatus || (j.Status == scheduledStatus && j.ScheduledAt != null && j.ScheduledAt <= now)
                                   || (j.Status == processingStatus && j.LastAttemptAt != null && j.LastAttemptAt < staleBefore))
                                  && (j.QueueName == null || j.QueueName == queueName),
                        orderBy: OrderBy<MongoJobDescriptorModel>.ByDescending(j => j.Priority).ThenBy(j => j.EnqueuedAt),
                        limit: 1,
                        ct: cancellationToken
                    ).ConfigureAwait(false);
                }
                else
                {
                    candidates = await _store.ReadAsync(
                        filter: j => j.Status == pendingStatus || (j.Status == scheduledStatus && j.ScheduledAt != null && j.ScheduledAt <= now)
                                  || (j.Status == processingStatus && j.LastAttemptAt != null && j.LastAttemptAt < staleBefore),
                        orderBy: OrderBy<MongoJobDescriptorModel>.ByDescending(j => j.Priority).ThenBy(j => j.EnqueuedAt),
                        limit: 1,
                        ct: cancellationToken
                    ).ConfigureAwait(false);
                }

                var candidate = candidates.FirstOrDefault();
                if (candidate == null)
                {
                    return null;
                }

                // Atomically claim: conditional update guarded on the still-eligible status. Mongo's
                // UpdateManyAsync applies $set per-document atomically, so only one racing worker's filter
                // matches after the first flips Status. Verify via ClaimToken since the API returns no count.
                var claimId = candidate.Guid;
                var originalStatus = candidate.Status;
                var claimToken = Guid.NewGuid();
                var seenAttempts = candidate.AttemptCount;

                // TASK-664 (TASK-575 on SQL): a reclaim must require the job to be STILL stale. A reclaim writes Processing
                // over Processing, so a status guard alone matched a second worker's identical update too; the winner sets
                // LastAttemptAt to now, so the stale condition admits one reclaim. A job abandoned with no attempts left
                // is buried as Dead under the same guard, so a slow worker's completion is never overwritten.
                var reclaim = originalStatus == processingStatus;
                Expression<Func<MongoJobDescriptorModel, bool>> stillEligible = reclaim
                    ? j => j.Guid == claimId && j.Status == processingStatus && j.LastAttemptAt != null && j.LastAttemptAt < staleBefore
                    : j => j.Guid == claimId && j.Status == originalStatus && j.AttemptCount == seenAttempts;

                if (reclaim && candidate.AttemptCount >= _retryPolicy.MaxRetriesFor(candidate.MaxRetries))
                {
                    await _store.UpdateAsync(
                        filter: stillEligible,
                        updates: new PropertyUpdate<MongoJobDescriptorModel>()
                            .Set(j => j.Status, (int)JobStatus.Dead)
                            .Set(j => j.CompletedAt, now)
                            .Set(j => j.LastError, JobReclaim.AbandonedError(_claimTimeout, candidate.AttemptCount)),
                        ct: cancellationToken
                    ).ConfigureAwait(false);
                    continue;
                }

                await _store.UpdateAsync(
                    filter: stillEligible, // TASK-158 (SH-M012) / TASK-664: see stillEligible above
                    updates: new PropertyUpdate<MongoJobDescriptorModel>()
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
                updates: new PropertyUpdate<MongoJobDescriptorModel>()
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
            var maxRetries = _retryPolicy.MaxRetriesFor(model.MaxRetries);
            var now = DateTime.UtcNow;
            var update = new PropertyUpdate<MongoJobDescriptorModel>().Set(j => j.LastError, error);
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
                updates: new PropertyUpdate<MongoJobDescriptorModel>()
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
                orderBy: OrderBy<MongoJobDescriptorModel>.ByDescending(j => j.EnqueuedAt),
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
