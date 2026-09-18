using System;
using Birko.Time;

namespace Birko.Data.EventSourcing.Events
{
    /// <summary>
    /// Default implementation of a domain event.
    /// </summary>
    public class DomainEvent : IEvent
    {
        /// <inheritdoc />
        public Guid EventId { get; set; }

        /// <inheritdoc />
        public Guid AggregateId { get; set; }

        /// <inheritdoc />
        public long Version { get; set; }

        /// <inheritdoc />
        public string EventType { get; set; } = string.Empty;

        /// <inheritdoc />
        public DateTime OccurredAt { get; set; }

        /// <inheritdoc />
        public string EventData { get; set; } = string.Empty;

        /// <inheritdoc />
        public string? Metadata { get; set; }

        /// <inheritdoc />
        public Guid? UserId { get; set; }

        /// <summary>
        /// Creates a new domain event for an aggregate.
        /// </summary>
        /// <param name="clock">Optional clock provider. Defaults to SystemDateTimeProvider.</param>
        public DomainEvent(IDateTimeProvider? clock = null)
        {
            EventId = Guid.NewGuid();
            OccurredAt = (clock ?? new SystemDateTimeProvider()).UtcNow;
        }

        /// <summary>
        /// Creates a new domain event with the specified data.
        /// </summary>
        /// <param name="aggregateId">The aggregate ID.</param>
        /// <param name="version">The expected version.</param>
        /// <param name="eventType">The type of event.</param>
        /// <param name="eventData">The event data as JSON.</param>
        /// <param name="userId">Optional user ID.</param>
        /// <param name="clock">Optional clock provider. Defaults to SystemDateTimeProvider.</param>
        public DomainEvent(Guid aggregateId, long version, string eventType, string eventData, Guid? userId = null, IDateTimeProvider? clock = null)
        {
            EventId = Guid.NewGuid();
            AggregateId = aggregateId;
            Version = version;
            EventType = eventType;
            EventData = eventData;
            OccurredAt = (clock ?? new SystemDateTimeProvider()).UtcNow;
            UserId = userId;
        }
    }
}
