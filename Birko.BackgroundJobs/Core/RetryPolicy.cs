using System;

namespace Birko.BackgroundJobs
{
    /// <summary>
    /// Defines how failed jobs should be retried.
    /// </summary>
    public class RetryPolicy
    {
        /// <summary>
        /// Maximum number of retry attempts. Default is 3.
        /// </summary>
        public int MaxRetries { get; set; } = 3;

        /// <summary>
        /// Base delay between retries. Multiplied by the attempt number for exponential backoff.
        /// Default is 30 seconds.
        /// </summary>
        public TimeSpan BaseDelay { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Maximum delay between retries. Default is 1 hour.
        /// </summary>
        public TimeSpan MaxDelay { get; set; } = TimeSpan.FromHours(1);

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

            // Compute the scaled delay in double and saturate at MaxDelay before allocating the
            // TimeSpan. The old (long)Math.Pow(...) * BaseDelay.Ticks overflowed to a negative tick
            // count for a large BaseDelay and/or attemptNumber, and a negative TimeSpan slipped past
            // the "> MaxDelay" clamp — scheduling the retry in the past (CR-L014).
            var scaledTicks = BaseDelay.Ticks * Math.Pow(2, attemptNumber - 1);
            if (scaledTicks >= MaxDelay.Ticks)
                return MaxDelay;
            return TimeSpan.FromTicks((long)scaledTicks);
        }

        /// <summary>
        /// Default retry policy: 3 retries with exponential backoff starting at 30s.
        /// </summary>
        /// <summary>
        /// How many attempts a job gets (TASK-662): its own <see cref="JobDescriptor.MaxRetries"/> when set, otherwise this
        /// policy's <see cref="MaxRetries"/>. The one producer every queue asks, so the nine backends cannot disagree again.
        /// </summary>
        public int MaxRetriesFor(int? jobMaxRetries) => jobMaxRetries ?? MaxRetries;

        public static RetryPolicy Default => new();

        /// <summary>
        /// No retries.
        /// </summary>
        public static RetryPolicy None => new() { MaxRetries = 0 };
    }
}
