using System;
using System.Collections.Generic;

namespace Birko.BackgroundJobs
{
    /// <summary>
    /// Provides context information for a running job.
    /// </summary>
    public class JobContext
    {
        /// <summary>
        /// The unique identifier of the job execution.
        /// </summary>
        public Guid JobId { get; }

        /// <summary>
        /// The number of times this job has been attempted (1-based).
        /// </summary>
        public int AttemptNumber { get; }

        /// <summary>
        /// When the job was originally enqueued.
        /// </summary>
        public DateTime EnqueuedAt { get; }

        /// <summary>
        /// Arbitrary metadata associated with this job execution.
        /// </summary>
        public IReadOnlyDictionary<string, string> Metadata { get; }

        public JobContext(Guid jobId, int attemptNumber, DateTime enqueuedAt, IReadOnlyDictionary<string, string>? metadata = null)
        {
            JobId = jobId;
            AttemptNumber = attemptNumber;
            EnqueuedAt = enqueuedAt;
            Metadata = metadata ?? new Dictionary<string, string>();
        }
    }
}
