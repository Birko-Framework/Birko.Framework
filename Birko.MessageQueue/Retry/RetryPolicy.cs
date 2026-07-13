using System;

namespace Birko.MessageQueue.Retry
{
    /// <summary>
    /// Defines how failed message deliveries should be retried.
    /// </summary>
    public class RetryPolicy
    {
        /// <summary>
        /// Maximum number of retry attempts. Default is 3.
        /// </summary>
        public int MaxRetries { get; set; } = 3;

        /// <summary>
        /// Base delay between retries. Default is 5 seconds.
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

            // CR-M199: compute the scaled delay in double and saturate at MaxDelay before converting to
            // ticks. The old `(long)Math.Pow(2, attemptNumber - 1)` overflowed to a negative value for
            // large attempt numbers (the cast wrapped to long.MinValue), producing a negative TimeSpan
            // that slipped past the `> MaxDelay` clamp — the same overflow fixed for Birko.Contracts
            // RetryPolicy under CR-M078.
            var scaledTicks = BaseDelay.Ticks * Math.Pow(2, attemptNumber - 1);
            return scaledTicks >= MaxDelay.Ticks
                ? MaxDelay
                : TimeSpan.FromTicks((long)scaledTicks);
        }

        /// <summary>
        /// Default retry policy: 3 retries with exponential backoff starting at 5s.
        /// </summary>
        public static RetryPolicy Default => new();

        /// <summary>
        /// No retries.
        /// </summary>
        public static RetryPolicy None => new() { MaxRetries = 0 };
    }
}
