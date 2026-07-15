using System;

namespace Birko.EventBus.Outbox
{
    /// <summary>
    /// Options for the outbox processor.
    /// </summary>
    public class OutboxOptions
    {
        /// <summary>
        /// Number of entries to process per batch. Default is 100.
        /// </summary>
        public int BatchSize { get; set; } = 100;

        /// <summary>
        /// How often the processor polls for pending entries. Default is 5 seconds.
        /// </summary>
        public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Maximum number of publish attempts before marking as Failed. Default is 5.
        /// </summary>
        public int MaxAttempts { get; set; } = 5;

        /// <summary>
        /// How long to keep published/failed entries before cleanup. Default is 7 days.
        /// </summary>
        public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromDays(7);

        /// <summary>
        /// How often the retention cleanup runs. Cleanup is a coarse retention prune (see
        /// <see cref="RetentionPeriod"/>), so it is throttled to this cadence rather than running on every
        /// <see cref="PollingInterval"/> — a full retention scan/delete every few seconds is needless work
        /// on a real store (CR-L259). Default is 1 hour.
        /// </summary>
        public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromHours(1);
    }
}
