using System;

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
        public RetryPolicy RetryPolicy { get; set; } = RetryPolicy.Default;

        /// <summary>
        /// Maximum time a single job is allowed to run before being cancelled.
        /// Default is 30 minutes.
        /// </summary>
        public TimeSpan JobTimeout { get; set; } = TimeSpan.FromMinutes(30);

        /// <summary>
        /// How long to keep completed/dead jobs before they are eligible for purging.
        /// Default is 7 days.
        /// </summary>
        public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromDays(7);
    }
}
