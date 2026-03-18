using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Time;

namespace Birko.BackgroundJobs.Processing
{
    /// <summary>
    /// In-memory job queue implementation. Suitable for single-process applications,
    /// testing, and development. Jobs are lost on process restart.
    /// </summary>
    public class InMemoryJobQueue : IJobQueue
    {
        private readonly ConcurrentDictionary<Guid, JobDescriptor> _jobs = new();
        private readonly SemaphoreSlim _lock = new(1, 1);
        private readonly RetryPolicy _retryPolicy;
        private readonly IDateTimeProvider _clock;

        public InMemoryJobQueue(IDateTimeProvider clock, RetryPolicy? retryPolicy = null)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _retryPolicy = retryPolicy ?? RetryPolicy.Default;
        }

        public Task<Guid> EnqueueAsync(JobDescriptor descriptor, CancellationToken cancellationToken = default)
        {
            _jobs[descriptor.Id] = descriptor;
            return Task.FromResult(descriptor.Id);
        }

        public async Task<JobDescriptor?> DequeueAsync(string? queueName = null, CancellationToken cancellationToken = default)
        {
            await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var now = _clock.UtcNow;

                var candidate = _jobs.Values
                    .Where(j => j.Status == JobStatus.Pending || (j.Status == JobStatus.Scheduled && j.ScheduledAt <= now))
                    .Where(j => queueName == null || j.QueueName == null || j.QueueName == queueName)
                    .OrderByDescending(j => j.Priority)
                    .ThenBy(j => j.EnqueuedAt)
                    .FirstOrDefault();

                if (candidate == null)
                {
                    return null;
                }

                candidate.Status = JobStatus.Processing;
                candidate.AttemptCount++;
                candidate.LastAttemptAt = now;

                return candidate;
            }
            finally
            {
                _lock.Release();
            }
        }

        public Task CompleteAsync(Guid jobId, CancellationToken cancellationToken = default)
        {
            if (_jobs.TryGetValue(jobId, out var descriptor))
            {
                descriptor.Status = JobStatus.Completed;
                descriptor.CompletedAt = _clock.UtcNow;
            }
            return Task.CompletedTask;
        }

        public Task FailAsync(Guid jobId, string error, CancellationToken cancellationToken = default)
        {
            if (_jobs.TryGetValue(jobId, out var descriptor))
            {
                descriptor.LastError = error;

                var maxRetries = descriptor.MaxRetries > 0 ? descriptor.MaxRetries : _retryPolicy.MaxRetries;

                if (descriptor.AttemptCount < maxRetries)
                {
                    var delay = _retryPolicy.GetDelay(descriptor.AttemptCount);
                    descriptor.Status = JobStatus.Scheduled;
                    descriptor.ScheduledAt = _clock.UtcNow.Add(delay);
                }
                else
                {
                    descriptor.Status = JobStatus.Dead;
                    descriptor.CompletedAt = _clock.UtcNow;
                }
            }
            return Task.CompletedTask;
        }

        public Task<bool> CancelAsync(Guid jobId, CancellationToken cancellationToken = default)
        {
            if (_jobs.TryGetValue(jobId, out var descriptor))
            {
                if (descriptor.Status == JobStatus.Pending || descriptor.Status == JobStatus.Scheduled)
                {
                    descriptor.Status = JobStatus.Cancelled;
                    descriptor.CompletedAt = _clock.UtcNow;
                    return Task.FromResult(true);
                }
            }
            return Task.FromResult(false);
        }

        public Task<JobDescriptor?> GetAsync(Guid jobId, CancellationToken cancellationToken = default)
        {
            _jobs.TryGetValue(jobId, out var descriptor);
            return Task.FromResult(descriptor);
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
                .Select(j => j.Id)
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
