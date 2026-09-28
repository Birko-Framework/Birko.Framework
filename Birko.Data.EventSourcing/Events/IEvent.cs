using System;

namespace Birko.Data.EventSourcing.Events
{
    /// <summary>
    /// Interface for domain events in event sourcing.
    /// </summary>
    public interface IEvent
    {
        /// <summary>
        /// Unique identifier for the event.
        /// </summary>
        Guid EventGuid { get; set; }

        /// <summary>
        /// The aggregate ID that this event relates to.
        /// </summary>
        Guid AggregateGuid { get; set; }

        /// <summary>
        /// The expected version of the aggregate after this event is applied.
        /// </summary>
        long Version { get; set; }

        /// <summary>
        /// The type of event (e.g., "Created", "Updated", "Deleted").
        /// </summary>
        string EventType { get; set; }

        /// <summary>
        /// When the event occurred.
        /// </summary>
        DateTime OccurredAt { get; set; }

        /// <summary>
        /// The event data as JSON.
        /// </summary>
        string EventData { get; set; }

        /// <summary>
        /// Optional metadata about the event.
        /// </summary>
        string? Metadata { get; set; }

        /// <summary>
        /// Optional user ID who caused the event.
        /// </summary>
        Guid? UserGuid { get; set; }
    }
}
