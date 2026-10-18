using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko;
using Birko.Time;

namespace Birko.BackgroundJobs.Processing
{
    /// <summary>
    /// In-memory job queue implementation. Suitable for single-process applications,
    /// testing, and development. Jobs are lost on process restart.
    /// </summary>
    public class InMemoryJobQueue : IJobQueue, IJobQueueCounts
    {
        private readonly ConcurrentDictionary<Guid, JobDescriptor> _jobs = new();
        private readonly SemaphoreSlim _lock = new(1, 1);
        private readonly RetryPolicy _retryPolicy;
        private readonly IDateTimeProvider _clock;
        private readonly TimeSpan _claimTimeout;

        /// <param name="claimTimeout">How long a job may stay Processing before it is presumed abandoned and offered
        /// again (TASK-664); <see cref="JobReclaim.DefaultClaimTimeout"/> when null.</param>
        public InMemoryJobQueue(IDateTimeProvider clock, RetryPolicy? retryPolicy = null, TimeSpan? claimTimeout = null)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _retryPolicy = retryPolicy ?? RetryPolicy.Default;
            _claimTimeout = JobReclaim.Resolve(claimTimeout);
        }

        public Task<Guid> EnqueueAsync(JobDescriptor descriptor, CancellationToken cancellationToken = default)
        {
            // Stamp EnqueuedAt from the injected clock so the whole queue flows through IDateTimeProvider
            // (the descriptor default is DateTime.UtcNow, set before it reaches the clock-aware queue) —
            // this makes enqueue time and the ThenBy(EnqueuedAt) dequeue ordering test-deterministic (CR-L017).
            descriptor.EnqueuedAt = _clock.UtcNow;
            _jobs[descriptor.Guid] = descriptor;
            return Task.FromResult(descriptor.Guid);
        }

        public async Task<JobDescriptor?> DequeueAsync(string? queueName = null, CancellationToken cancellationToken = default)
        {
            await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var now = _clock.UtcNow;
                var staleBefore = now - _claimTimeout;

                while (true)
                {
                    // TASK-664: a job left Processing longer than the claim timeout is offered again, as on SQL.
                    var candidate = _jobs.Values
                        .Where(j => j.Status == JobStatus.Pending || (j.Status == JobStatus.Scheduled && j.ScheduledAt <= now)
                                 || (j.Status == JobStatus.Processing && j.LastAttemptAt != null && j.LastAttemptAt < staleBefore))
                        .Where(j => queueName == null || j.QueueName == null || j.QueueName == queueName)
                        .OrderByDescending(j => j.Priority)
                        .ThenBy(j => j.EnqueuedAt)
                        .FirstOrDefault();

                    if (candidate == null)
                    {
                        return null;
                    }

                    if (candidate.Status == JobStatus.Processing && candidate.AttemptCount >= _retryPolicy.MaxRetriesFor(candidate.MaxRetries))
                    {
                        candidate.LastError = JobReclaim.AbandonedError(_claimTimeout, candidate.AttemptCount);
                        candidate.CompletedAt = now;
                        candidate.Status = JobStatus.Dead;
                        continue;
                    }

                    candidate.Status = JobStatus.Processing;
                    candidate.AttemptCount++;
                    candidate.LastAttemptAt = now;

                    return candidate;
                }
            }
            finally
            {
                _lock.Release();
            }
        }

        // TASK-158 (SH-M011): the transitions take the lock DequeueAsync reads under. They mutated the shared descriptor
        // unlocked, and FailAsync wrote Status = Scheduled before ScheduledAt, so a concurrent dequeue could see Scheduled
        // with the previous, already elapsed ScheduledAt and claim the retry before its backoff.
        public Task CompleteAsync(Guid jobId, CancellationToken cancellationToken = default)
        {
            _lock.Wait(CancellationToken.None);
            try
            {
                // Only a claimed (Processing) job is completed or failed, as on every store-backed queue (TASK-158).
                if (_jobs.TryGetValue(jobId, out var descriptor) && descriptor.Status == JobStatus.Processing)
                {
                    descriptor.CompletedAt = _clock.UtcNow;
                    descriptor.Status = JobStatus.Completed;
                }
            }
            finally
            {
                _lock.Release();
            }
            return Task.CompletedTask;
        }

        public Task FailAsync(Guid jobId, string error, CancellationToken cancellationToken = default)
        {
            _lock.Wait(CancellationToken.None);
            try
            {
            if (_jobs.TryGetValue(jobId, out var descriptor) && descriptor.Status == JobStatus.Processing)
            {
                descriptor.LastError = error;

                var maxRetries = _retryPolicy.MaxRetriesFor(descriptor.MaxRetries);

                if (descriptor.AttemptCount < maxRetries)
                {
                    var delay = _retryPolicy.GetDelay(descriptor.AttemptCount);
                    descriptor.ScheduledAt = _clock.UtcNow.Add(delay);
                    descriptor.Status = JobStatus.Scheduled;
                }
                else
                {
                    descriptor.CompletedAt = _clock.UtcNow;
                    descriptor.Status = JobStatus.Dead;
                }
            }
            }
            finally
            {
                _lock.Release();
            }
            return Task.CompletedTask;
        }

        public Task<bool> CancelAsync(Guid jobId, CancellationToken cancellationToken = default)
        {
            _lock.Wait(CancellationToken.None);
            try
            {
                if (_jobs.TryGetValue(jobId, out var descriptor)
                    && (descriptor.Status == JobStatus.Pending || descriptor.Status == JobStatus.Scheduled))
                {
                    descriptor.CompletedAt = _clock.UtcNow;
                    descriptor.Status = JobStatus.Cancelled;
                    return Task.FromResult(true);
                }
            }
            finally
            {
                _lock.Release();
            }
            return Task.FromResult(false);
        }

        public Task<JobDescriptor?> GetAsync(Guid jobId, CancellationToken cancellationToken = default)
        {
            _jobs.TryGetValue(jobId, out var descriptor);
            return Task.FromResult(descriptor);
        }

        /// <summary>
        /// Counts by status without materialising the jobs — see <see cref="IJobQueueCounts"/> for why
        /// taking the length of <see cref="GetByStatusAsync"/> is not the same thing (it is capped).
        /// </summary>
        public Task<IReadOnlyDictionary<JobStatus, int>> CountByStatusAsync(CancellationToken cancellationToken = default)
        {
            var counts = _jobs.Values
                .GroupBy(j => j.Status)
                .ToDictionary(g => g.Key, g => g.Count());

            return Task.FromResult<IReadOnlyDictionary<JobStatus, int>>(counts);
        }

        public Task<IReadOnlyList<JobDescriptor>> GetByStatusAsync(JobStatus status, int limit = 100, CancellationToken cancellationToken = default)
        {
            var result = _jobs.Values
                .Where(j => j.Status == status)
                .OrderByDescending(j => j.EnqueuedAt)
                .Take(limit)
                .ToList();

            return Task.FromResult<IReadOnlyList<JobDescriptor>>(result);
        }

        public Task<int> PurgeAsync(TimeSpan olderThan, CancellationToken cancellationToken = default)
        {
            var cutoff = _clock.UtcNow.Subtract(olderThan);
            var count = 0;

            var toPurge = _jobs.Values
                .Where(j => (j.Status == JobStatus.Completed || j.Status == JobStatus.Dead || j.Status == JobStatus.Cancelled)
                         && j.CompletedAt.HasValue && j.CompletedAt.Value < cutoff)
                .Select(j => j.Guid)
                .ToList();

            foreach (var id in toPurge)
            {
                if (_jobs.TryRemove(id, out _))
                {
                    count++;
                }
            }

            return Task.FromResult(count);
        }
    }
}
