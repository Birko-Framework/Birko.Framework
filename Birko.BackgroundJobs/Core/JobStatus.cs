namespace Birko.BackgroundJobs
{
    /// <summary>
    /// Represents the lifecycle status of a background job.
    /// </summary>
    public enum JobStatus
    {
        /// <summary>
        /// Job is enqueued and waiting to be processed.
        /// </summary>
        Pending = 0,

        /// <summary>
        /// Job is scheduled for future execution (has a Delay).
        /// </summary>
        Scheduled = 1,

        /// <summary>
        /// Job is currently being processed by a worker.
        /// </summary>
        Processing = 2,

        /// <summary>
        /// Job completed successfully.
        /// </summary>
        Completed = 3,

        /// <summary>
        /// Job failed but will be retried.
        /// </summary>
        Failed = 4,

        /// <summary>
        /// Job failed after exhausting all retry attempts.
        /// </summary>
        Dead = 5,

        /// <summary>
        /// Job was cancelled before completion.
        /// </summary>
        Cancelled = 6
    }
}
