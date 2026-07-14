using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.BackgroundJobs.JSON.Models;
using Birko.Data.JSON.Stores;
using Birko.Data.Stores;
using Birko.Configuration;
using Birko.Time;

namespace Birko.BackgroundJobs.JSON
{
    /// <summary>
    /// JSON file-based job queue using Birko.Data.JSON stores.
    /// Good for development, testing, and single-process deployments.
    /// </summary>
    public class JsonJobQueue : IJobQueue
    {
        private readonly AsyncJsonStore<JsonJobDescriptorModel> _store;
        private readonly RetryPolicy _retryPolicy;
        private readonly IDateTimeProvider _clock;

        // Serializes the read-claim-update in DequeueAsync so two worker tasks in the same process
        // cannot claim the same job (CR-M018) — mirrors the reference InMemoryJobQueue. The file
        // store has no compare-and-swap, so cross-process concurrency remains unsupported by design.
        private readonly SemaphoreSlim _dequeueLock = new(1, 1);

        /// <summary>
        /// Creates a new JSON job queue.
        /// </summary>
        public JsonJobQueue(Birko.Configuration.Settings settings, IDateTimeProvider clock, RetryPolicy? retryPolicy = null)
        {
            _store = new AsyncJsonStore<JsonJobDescriptorModel>();
            _store.SetSettings(settings);
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _retryPolicy = retryPolicy ?? RetryPolicy.Default;
        }

        /// <summary>
        /// Creates a new JSON job queue from an existing store.
        /// </summary>
        public JsonJobQueue(AsyncJsonStore<JsonJobDescriptorModel> store, IDateTimeProvider clock, RetryPolicy? retryPolicy = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _retryPolicy = retryPolicy ?? RetryPolicy.Default;
        }

        /// <summary>
        /// Gets the underlying store for advanced scenarios.
        /// </summary>
        public AsyncJsonStore<JsonJobDescriptorModel> Store => _store;

        public async Task<Guid> EnqueueAsync(JobDescriptor descriptor, CancellationToken cancellationToken = default)
        {
            var model = JsonJobDescriptorModel.FromDescriptor(descriptor);
            var id = await _store.CreateAsync(model, ct: cancellationToken).ConfigureAwait(false);
            return id;
        }

        public async Task<JobDescriptor?> DequeueAsync(string? queueName = null, CancellationToken cancellationToken = default)
        {
            await _dequeueLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var now = _clock.UtcNow;
                var pendingStatus = (int)JobStatus.Pending;
                var scheduledStatus = (int)JobStatus.Scheduled;

                IEnumerable<JsonJobDescriptorModel> candidates;

                if (queueName != null)
                {
                    candidates = await _store.ReadAsync(
                        filter: j => (j.Status == pendingStatus || (j.Status == scheduledStatus && j.ScheduledAt != null && j.ScheduledAt <= now))
                                  && (j.QueueName == null || j.QueueName == queueName),
                        orderBy: OrderBy<JsonJobDescriptorModel>.ByDescending(j => j.Priority).ThenBy(j => j.EnqueuedAt),
                        limit: 1,
                        ct: cancellationToken
                    ).ConfigureAwait(false);
                }
                else
                {
                    candidates = await _store.ReadAsync(
                        filter: j => j.Status == pendingStatus || (j.Status == scheduledStatus && j.ScheduledAt != null && j.ScheduledAt <= now),
                        orderBy: OrderBy<JsonJobDescriptorModel>.ByDescending(j => j.Priority).ThenBy(j => j.EnqueuedAt),
                        limit: 1,
                        ct: cancellationToken
                    ).ConfigureAwait(false);
                }

                var candidate = candidates.FirstOrDefault();
                if (candidate == null)
                {
                    return null;
                }

                candidate.Status = (int)JobStatus.Processing;
                candidate.AttemptCount++;
                candidate.LastAttemptAt = _clock.UtcNow;

                await _store.UpdateAsync(candidate, ct: cancellationToken).ConfigureAwait(false);

                return candidate.ToDescriptor();
            }
            finally
            {
                _dequeueLock.Release();
            }
        }

        public async Task CompleteAsync(Guid jobId, CancellationToken cancellationToken = default)
        {
            var model = await _store.ReadAsync(j => j.Guid == jobId, cancellationToken).ConfigureAwait(false);
            if (model == null) return;

            model.Status = (int)JobStatus.Completed;
            model.CompletedAt = _clock.UtcNow;

            await _store.UpdateAsync(model, ct: cancellationToken).ConfigureAwait(false);
        }

        public async Task FailAsync(Guid jobId, string error, CancellationToken cancellationToken = default)
        {
            var model = await _store.ReadAsync(j => j.Guid == jobId, cancellationToken).ConfigureAwait(false);
            if (model == null) return;

            model.LastError = error;

            // Fall back to the queue's RetryPolicy.MaxRetries when the job's own MaxRetries is 0,
            // mirroring the reference InMemoryJobQueue — otherwise a MaxRetries==0 job always went
            // straight to Dead and the injected RetryPolicy.MaxRetries was never read (CR-L025).
            var maxRetries = model.MaxRetries > 0 ? model.MaxRetries : _retryPolicy.MaxRetries;
            if (model.AttemptCount < maxRetries)
            {
                var delay = _retryPolicy.GetDelay(model.AttemptCount);
                model.Status = (int)JobStatus.Scheduled;
                model.ScheduledAt = _clock.UtcNow.Add(delay);
            }
            else
            {
                model.Status = (int)JobStatus.Dead;
                model.CompletedAt = _clock.UtcNow;
            }

            await _store.UpdateAsync(model, ct: cancellationToken).ConfigureAwait(false);
        }

        public async Task<bool> CancelAsync(Guid jobId, CancellationToken cancellationToken = default)
        {
            var pendingStatus = (int)JobStatus.Pending;
            var scheduledStatus = (int)JobStatus.Scheduled;

            var model = await _store.ReadAsync(
                j => j.Guid == jobId && (j.Status == pendingStatus || j.Status == scheduledStatus),
                cancellationToken
            ).ConfigureAwait(false);

            if (model == null) return false;

            model.Status = (int)JobStatus.Cancelled;
            model.CompletedAt = _clock.UtcNow;

            await _store.UpdateAsync(model, ct: cancellationToken).ConfigureAwait(false);
            return true;
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
                orderBy: OrderBy<JsonJobDescriptorModel>.ByDescending(j => j.EnqueuedAt),
                limit: limit,
                ct: cancellationToken
            ).ConfigureAwait(false);

            return models.Select(m => m.ToDescriptor()).ToList();
        }

        public async Task<int> PurgeAsync(TimeSpan olderThan, CancellationToken cancellationToken = default)
        {
            var cutoff = _clock.UtcNow.Subtract(olderThan);
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
