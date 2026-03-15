using System;

namespace Birko.EventBus
{
    /// <summary>
    /// Base record for events. Provides EventId, OccurredAt, CorrelationId, and Source.
    /// Derive concrete events as sealed records for immutability.
    /// </summary>
    public abstract record EventBase : IEvent
    {
        /// <inheritdoc />
        public Guid EventId { get; init; } = Guid.NewGuid();

        /// <inheritdoc />
        public DateTime OccurredAt { get; init; } = DateTime.UtcNow;

        /// <summary>
        /// Correlation ID for tracing related events across handlers and services.
        /// </summary>
        public Guid? CorrelationId { get; init; }

        /// <inheritdoc />
        public abstract string Source { get; }
    }
}
