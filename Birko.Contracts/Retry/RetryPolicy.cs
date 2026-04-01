using System;

namespace Birko
{
    /// <summary>
    /// Defines how failed operations should be retried with optional exponential backoff.
    /// Shared by BackgroundJobs, MessageQueue, and other retry-aware subsystems.
    /// </summary>
    public class RetryPolicy
    {
        private static readonly System.Random _jitterRandom = new();
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
        /// Multiplier for exponential backoff. Default is 2.0.
        /// </summary>
        public double BackoffMultiplier { get; set; } = 2.0;

        /// <summary>
        /// Whether to add random jitter (+-25%) to delays to prevent thundering herd. Default is false.
        /// </summary>
        public bool AddJitter { get; set; } = false;

        /// <summary>
        /// Calculates the delay before the next retry attempt.
        /// </summary>
        public TimeSpan GetDelay(int attemptNumber)
        {
            TimeSpan delay;
            if (!UseExponentialBackoff)
            {
                delay = BaseDelay;
            }
            else
            {
                delay = TimeSpan.FromTicks(BaseDelay.Ticks * (long)Math.Pow(BackoffMultiplier, attemptNumber - 1));
                if (delay > MaxDelay)
                    delay = MaxDelay;
            }

            if (!AddJitter)
                return delay;

            var jitterFactor = 0.75 + (_jitterRandom.NextDouble() * 0.5);
            return TimeSpan.FromTicks((long)(delay.Ticks * jitterFactor));
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
