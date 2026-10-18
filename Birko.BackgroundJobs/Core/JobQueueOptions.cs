using System;
using Birko;

namespace Birko.BackgroundJobs
{
    /// <summary>
    /// Configuration options for the background job processor.
    /// </summary>
    public class JobQueueOptions
    {
        /// <summary>
        /// How often the processor polls for new jobs. Default is 1 second.
        /// </summary>
        public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Maximum number of concurrent jobs. Default is 4.
        /// </summary>
        public int MaxConcurrency { get; set; } = 4;

        /// <summary>
        /// Default queue name. Default is "default".
        /// </summary>
        public string DefaultQueueName { get; set; } = "default";

        /// <summary>
        /// Retry policy for the in-memory queue <c>AddBackgroundJobs</c> registers (Birko.BackgroundJobs.Hosting). A queue
        /// you construct yourself takes its own <c>RetryPolicy</c> constructor argument; nothing else reads this (SH-L009).
        /// </summary>
        public RetryPolicy RetryPolicy { get; set; } = new()
        {
            BaseDelay = TimeSpan.FromSeconds(30),
            MaxDelay = TimeSpan.FromHours(1)
        };

        /// <summary>
        /// Maximum time a single job is allowed to run before being cancelled.
        /// Default is 30 minutes, the queues' default claim timeout (<see cref="JobReclaim.DefaultClaimTimeout"/>): a job
        /// held longer than that is presumed abandoned and offered again, so raising this needs the queue's raised too.
        /// </summary>
        public TimeSpan JobTimeout { get; set; } = JobReclaim.DefaultClaimTimeout;

        /// <summary>
        /// How long to keep completed, dead and cancelled jobs before the purge removes them. Default is 7 days. Used only
        /// when <see cref="PurgeInterval"/> is set (SH-L010).
        /// </summary>
        public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromDays(7);

        /// <summary>
        /// How often the processor purges terminal jobs older than <see cref="RetentionPeriod"/>. <c>null</c> — the
        /// default — purges nothing, so nothing is deleted unless a host opts in (SH-L010, decided by the user 2026-10-18).
        /// </summary>
        public TimeSpan? PurgeInterval { get; set; }
    }
}
