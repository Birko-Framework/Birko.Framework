using System;
using DomainEvent = Birko.Data.EventSourcing.Events.IEvent;

namespace Birko.EventBus.EventSourcing
{
    /// <summary>
    /// Event bus event raised when a domain event is appended to the event store.
    /// Wraps the domain event from the event sourcing layer so it can be
    /// published through the event bus to other modules/services.
    /// </summary>
    public sealed record DomainEventPublished : EventBase
    {
        /// <summary>
        /// The aggregate ID this domain event relates to.
        /// </summary>
        public Guid AggregateId { get; init; }

        /// <summary>
        /// The aggregate version after this event.
        /// </summary>
        public long Version { get; init; }

        /// <summary>
        /// The domain event type descriptor (e.g., "Created", "Updated", "Deleted").
        /// </summary>
        public string DomainEventType { get; init; } = null!;

        /// <summary>
        /// The serialized event data from the domain event.
        /// </summary>
        public string EventData { get; init; } = null!;

        /// <summary>
        /// Optional metadata from the domain event.
        /// </summary>
        public string? Metadata { get; init; }

        /// <summary>
        /// The user who caused the domain event, if tracked.
        /// </summary>
        public Guid? UserId { get; init; }

        public override string Source => "event-sourcing";

        /// <summary>
        /// Creates a DomainEventPublished from an event sourcing domain event.
        /// </summary>
        public DomainEventPublished(DomainEvent domainEvent)
        {
            AggregateId = domainEvent.AggregateId;
            Version = domainEvent.Version;
            DomainEventType = domainEvent.EventType;
            EventData = domainEvent.EventData;
            Metadata = domainEvent.Metadata;
            UserId = domainEvent.UserId;

            // CR-M186: preserve the domain event's original timestamp and identity instead of letting
            // EventBase stamp the current wall-clock time and a fresh Guid. During replay
            // (EventReplayService) this keeps projections/read models seeing the historical event time
            // and the stable EventId, so time-ordered and idempotent (dedup-by-EventId) rebuilds work.
            OccurredAt = domainEvent.OccurredAt;
            EventId = domainEvent.EventId;
        }

        /// <summary>
        /// Parameterless constructor for deserialization.
        /// </summary>
        public DomainEventPublished() { }
    }
}
