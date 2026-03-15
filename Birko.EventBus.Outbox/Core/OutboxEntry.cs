using System;
using System.Collections.Generic;

namespace Birko.EventBus.Outbox
{
    /// <summary>
    /// Represents a persisted event in the outbox, waiting to be published.
    /// </summary>
    public class OutboxEntry
    {
        /// <summary>
        /// Unique identifier for this outbox entry.
        /// </summary>
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// The event's unique identifier (from IEvent.EventId).
        /// </summary>
        public Guid EventId { get; set; }

        /// <summary>
        /// Assembly-qualified type name of the event (for deserialization).
        /// </summary>
        public string EventType { get; set; } = null!;

        /// <summary>
        /// Serialized event payload (JSON).
        /// </summary>
        public string Payload { get; set; } = null!;

        /// <summary>
        /// Source module or component that raised the event.
        /// </summary>
        public string Source { get; set; } = null!;

        /// <summary>
        /// Correlation ID for distributed tracing.
        /// </summary>
        public Guid? CorrelationId { get; set; }

        /// <summary>
        /// Tenant identifier, if multi-tenancy is enabled.
        /// </summary>
        public Guid? TenantGuid { get; set; }

        /// <summary>
        /// Additional metadata headers.
        /// </summary>
        public Dictionary<string, string> Headers { get; set; } = [];

        /// <summary>
        /// Current status of this entry.
        /// </summary>
        public OutboxStatus Status { get; set; } = OutboxStatus.Pending;

        /// <summary>
        /// When the entry was created (UTC).
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When the entry was successfully published (UTC). Null if not yet published.
        /// </summary>
        public DateTime? PublishedAt { get; set; }

        /// <summary>
        /// Number of publish attempts made.
        /// </summary>
        public int Attempts { get; set; }

        /// <summary>
        /// Error message from the last failed attempt.
        /// </summary>
        public string? LastError { get; set; }
    }
}
