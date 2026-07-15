using Birko.Redis;

namespace Birko.MessageQueue.Redis
{
    /// <summary>
    /// Configuration settings for the Redis Streams message queue.
    /// Extends RedisSettings with stream-specific options.
    /// </summary>
    public class RedisStreamSettings : RedisSettings
    {
        /// <summary>
        /// Gets or sets the consumer group name for XREADGROUP.
        /// If null, XREAD is used instead (no consumer groups).
        /// </summary>
        public string? ConsumerGroup { get; set; }

        /// <summary>
        /// Gets or sets the consumer name within a consumer group.
        /// Auto-generated if null.
        /// </summary>
        public string? ConsumerName { get; set; }

        /// <summary>
        /// Gets or sets the maximum number of messages to read per XREAD/XREADGROUP call.
        /// Default is 10.
        /// </summary>
        public int ReadCount { get; set; } = 10;

        /// <summary>
        /// Empty-poll back-off interval in milliseconds. Null falls back to 1000ms.
        /// </summary>
        /// <remarks>
        /// CR-L292: this is NOT a server-side blocking read. StackExchange.Redis's
        /// <c>StreamReadAsync</c>/<c>StreamReadGroupAsync</c> don't expose the XREAD <c>BLOCK</c> option, so
        /// the consumer does a non-blocking count-limited read and, when the stream is empty, waits this long
        /// via <c>Task.Delay</c> before polling again. Delivery latency for a newly-arrived message is
        /// therefore bounded below by this interval; lower it for lower latency at the cost of more polls.
        /// </remarks>
        public int? BlockMilliseconds { get; set; } = 5000;

        /// <summary>
        /// Gets or sets the maximum stream length. When exceeded, older entries are trimmed.
        /// Null means no trimming. Uses MAXLEN with approximate (~) trimming.
        /// </summary>
        public int? MaxStreamLength { get; set; }

        /// <summary>
        /// Gets or sets whether to automatically create consumer groups on first subscribe.
        /// Default is true.
        /// </summary>
        public bool AutoCreateConsumerGroup { get; set; } = true;

        /// <summary>
        /// Minimum idle time (ms) before an unacknowledged pending entry becomes eligible
        /// for reclaim/redelivery via XAUTOCLAIM. This also throttles retries of a
        /// permanently-failing message (it cannot be re-processed more often than this).
        /// Set to 0 to disable pending-entry reclaim entirely. Default is 30 seconds.
        /// </summary>
        public long PendingRetryMilliseconds { get; set; } = 30_000;

        /// <summary>
        /// Gets or sets the stream key prefix for destinations.
        /// Default is "birko:mq:stream".
        /// </summary>
        public string StreamPrefix { get; set; } = "birko:mq:stream";

        /// <summary>
        /// Initializes a new instance with default values (localhost:6379).
        /// </summary>
        public RedisStreamSettings() : base()
        {
        }

        /// <summary>
        /// Initializes a new instance with host and port.
        /// </summary>
        public RedisStreamSettings(string host, int port = 6379, string? password = null, int database = 0, bool useSsl = false)
            : base(host, port, password, database, useSsl)
        {
        }

        /// <summary>
        /// Gets the Redis stream key for a given destination.
        /// </summary>
        /// <param name="destination">The logical destination name.</param>
        /// <returns>The full Redis key for the stream.</returns>
        public string GetStreamKey(string destination)
        {
            return $"{StreamPrefix}:{destination}";
        }
    }
}
