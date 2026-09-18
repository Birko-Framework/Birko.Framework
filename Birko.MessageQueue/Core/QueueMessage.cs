using System;

namespace Birko.MessageQueue
{
    /// <summary>
    /// Represents a message in the queue.
    /// </summary>
    public class QueueMessage
    {
        /// <summary>
        /// Unique message identifier.
        /// </summary>
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// The serialized message body.
        /// </summary>
        public string Body { get; set; } = string.Empty;

        /// <summary>
        /// The .NET type name of the payload, used for deserialization.
        /// </summary>
        public string? PayloadType { get; set; }

        /// <summary>
        /// Message headers/metadata.
        /// </summary>
        public MessageHeaders Headers { get; set; } = new();

        /// <summary>
        /// When the message was created.
        /// </summary>
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>
        /// Optional delay before the message becomes visible to consumers.
        /// </summary>
        public TimeSpan? Delay { get; set; }

        /// <summary>
        /// Optional message expiration (time-to-live).
        /// </summary>
        public TimeSpan? TimeToLive { get; set; }

        /// <summary>
        /// Message priority (higher = more urgent). Default is 0 (normal).
        /// </summary>
        public int Priority { get; set; }
    }
}
