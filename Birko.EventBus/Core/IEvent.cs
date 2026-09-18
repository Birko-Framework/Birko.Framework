using System;

namespace Birko.EventBus
{
    /// <summary>
    /// Marker interface for all events.
    /// </summary>
    public interface IEvent
    {
        /// <summary>
        /// Unique identifier for this event instance.
        /// </summary>
        Guid EventId { get; }

        /// <summary>
        /// When the event occurred (UTC).
        /// </summary>
        DateTime OccurredAt { get; }

        /// <summary>
        /// Source module or component that raised the event.
        /// </summary>
        string Source { get; }
    }
}
