using System;
using System.Collections.Generic;

namespace Birko.EventBus.MessageQueue
{
    /// <summary>
    /// Wraps an <see cref="IEvent"/> with transport metadata for serialization over a message queue.
    /// The envelope is what gets serialized into <see cref="Birko.MessageQueue.QueueMessage.Body"/>.
    /// </summary>
    public class EventEnvelope
    {
        /// <summary>
        /// The event's unique identifier.
        /// </summary>
        public Guid EventId { get; set; }

        /// <summary>
        /// Assembly-qualified type name of the event (for deserialization).
        /// </summary>
        public string EventType { get; set; } = null!;

        /// <summary>
        /// Source module or component that raised the event.
        /// </summary>
        public string Source { get; set; } = null!;

        /// <summary>
        /// When the event occurred (UTC).
        /// </summary>
        public DateTime OccurredAt { get; set; }

        /// <summary>
        /// Correlation ID for distributed tracing.
        /// </summary>
        public Guid? CorrelationId { get; set; }

        /// <summary>
        /// Tenant identifier, if multi-tenancy is enabled.
        /// </summary>
        public Guid? TenantGuid { get; set; }

        /// <summary>
        /// Serialized event payload (JSON).
        /// </summary>
        public string Payload { get; set; } = null!;

        /// <summary>
        /// Additional metadata headers.
        /// </summary>
        public Dictionary<string, string> Headers { get; set; } = [];
    }
}
