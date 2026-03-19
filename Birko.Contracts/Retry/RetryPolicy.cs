using System;

namespace Birko
{
    /// <summary>
    /// Defines how failed operations should be retried with optional exponential backoff.
    /// Shared by BackgroundJobs, MessageQueue, and other retry-aware subsystems.
    /// </summary>
    public class RetryPolicy
    {
        /// <summary>
        /// Maximum number of retry attempts. Default is 3.
        /// </summary>
        public int MaxRetries { get; set; } = 3;

        /// <summary>
        /// Base delay between retries. Multiplied exponentially per attempt when UseExponentialBackoff is true.
        /// Default is 5 seconds.
        /// </summary>
        public TimeSpan BaseDelay { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Maximum delay between retries. Default is 5 minutes.
        /// </summary>
        public TimeSpan MaxDelay { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Whether to use exponential backoff. Default is true.
        /// When false, BaseDelay is used as a fixed delay.
        /// </summary>
        public bool UseExponentialBackoff { get; set; } = true;

        /// <summary>
        /// Calculates the delay before the next retry attempt.
        /// </summary>
        public TimeSpan GetDelay(int attemptNumber)
        {
            if (!UseExponentialBackoff)
            {
                return BaseDelay;
            }

            var delay = TimeSpan.FromTicks(BaseDelay.Ticks * (long)Math.Pow(2, attemptNumber - 1));
            return delay > MaxDelay ? MaxDelay : delay;
        }

        /// <summary>
        /// Default retry policy: 3 retries with exponential backoff starting at 5s, max 5min.
        /// </summary>
        public static RetryPolicy Default => new();

        /// <summary>
        /// No retries.
        /// </summary>
        public static RetryPolicy None => new() { MaxRetries = 0 };
    }
}
