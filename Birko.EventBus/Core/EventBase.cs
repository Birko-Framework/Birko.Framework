using System;
using Birko.Time;

namespace Birko.EventBus
{
    /// <summary>
    /// Base record for events. Provides EventId, OccurredAt, CorrelationId, and Source.
    /// Derive concrete events as sealed records for immutability.
    /// </summary>
    public abstract record EventBase : IEvent
    {
        /// <summary>
        /// Default clock used when no explicit provider is supplied.
        /// Replace with a custom implementation for testing.
        /// </summary>
        public static IDateTimeProvider DefaultClock { get; set; } = new SystemDateTimeProvider();

        /// <inheritdoc />
        public Guid EventId { get; init; } = Guid.NewGuid();

        /// <inheritdoc />
        public DateTime OccurredAt { get; init; } = DefaultClock.UtcNow;

        /// <summary>
        /// Correlation ID for tracing related events across handlers and services.
        /// </summary>
        public Guid? CorrelationId { get; init; }

        /// <inheritdoc />
        public abstract string Source { get; }
    }
}
