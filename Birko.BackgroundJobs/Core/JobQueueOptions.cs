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
        /// Default retry policy for jobs that don't specify their own.
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
        /// How long to keep completed/dead jobs before they are eligible for purging.
        /// Default is 7 days.
        /// </summary>
        public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromDays(7);
    }
}
