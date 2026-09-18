using System;
using System.Collections.Generic;

namespace Birko.BackgroundJobs
{
    /// <summary>
    /// Describes a job to be enqueued, including its type, serialized input, and execution options.
    /// </summary>
    public class JobDescriptor
    {
        /// <summary>
        /// Unique identifier for this job.
        /// </summary>
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Assembly-qualified type name of the job class.
        /// </summary>
        public string JobType { get; set; } = string.Empty;

        /// <summary>
        /// Serialized input data (null for parameterless jobs).
        /// </summary>
        public string? SerializedInput { get; set; }

        /// <summary>
        /// Assembly-qualified type name of the input class (null for parameterless jobs).
        /// </summary>
        public string? InputType { get; set; }

        /// <summary>
        /// Optional queue name. Null uses the default queue.
        /// </summary>
        public string? QueueName { get; set; }

        /// <summary>
        /// Priority (higher = processed first). Default is 0.
        /// </summary>
        public int Priority { get; set; }

        /// <summary>
        /// Maximum number of retry attempts on failure. Default is 3.
        /// </summary>
        public int MaxRetries { get; set; } = 3;

        /// <summary>
        /// Delay before the job becomes eligible for processing.
        /// </summary>
        public TimeSpan? Delay { get; set; }

        /// <summary>
        /// When the job was enqueued.
        /// </summary>
        public DateTime EnqueuedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Earliest time the job can be processed (EnqueuedAt + Delay).
        /// </summary>
        public DateTime? ScheduledAt { get; set; }

        /// <summary>
        /// Current status of the job.
        /// </summary>
        public JobStatus Status { get; set; } = JobStatus.Pending;

        /// <summary>
        /// Number of times this job has been attempted.
        /// </summary>
        public int AttemptCount { get; set; }

        /// <summary>
        /// When the job was last attempted.
        /// </summary>
        public DateTime? LastAttemptAt { get; set; }

        /// <summary>
        /// When the job completed (success or final failure).
        /// </summary>
        public DateTime? CompletedAt { get; set; }

        /// <summary>
        /// Error message from the last failed attempt.
        /// </summary>
        public string? LastError { get; set; }

        /// <summary>
        /// Arbitrary metadata for the job.
        /// </summary>
        public Dictionary<string, string> Metadata { get; set; } = new();
    }
}
