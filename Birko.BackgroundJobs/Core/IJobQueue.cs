using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.BackgroundJobs
{
    /// <summary>
    /// Provides storage and retrieval of job descriptors.
    /// </summary>
    public interface IJobQueue
    {
        /// <summary>
        /// Enqueues a job for processing.
        /// </summary>
        Task<Guid> EnqueueAsync(JobDescriptor descriptor, CancellationToken cancellationToken = default);

        /// <summary>
        /// Dequeues the next available job from the specified queue.
        /// Returns null if no jobs are available.
        /// </summary>
        Task<JobDescriptor?> DequeueAsync(string? queueName = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Marks a job as completed successfully.
        /// </summary>
        Task CompleteAsync(Guid jobId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Marks a job as failed. If retries remain, re-enqueues it with a delay.
        /// </summary>
        Task FailAsync(Guid jobId, string error, CancellationToken cancellationToken = default);

        /// <summary>
        /// Cancels a pending or scheduled job.
        /// </summary>
        Task<bool> CancelAsync(Guid jobId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the current descriptor of a job by its ID.
        /// </summary>
        Task<JobDescriptor?> GetAsync(Guid jobId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets all jobs with the specified status.
        /// </summary>
        Task<IReadOnlyList<JobDescriptor>> GetByStatusAsync(JobStatus status, int limit = 100, CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes completed and dead jobs older than the specified age.
        /// Returns the number of jobs purged.
        /// </summary>
        Task<int> PurgeAsync(TimeSpan olderThan, CancellationToken cancellationToken = default);
    }
}
