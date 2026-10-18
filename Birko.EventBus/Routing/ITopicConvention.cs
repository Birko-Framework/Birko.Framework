using System;

namespace Birko.EventBus.Routing
{
    /// <summary>
    /// Determines the topic/destination name for an event type.
    /// Used by distributed event bus to map event types to message queue destinations.
    /// </summary>
    public interface ITopicConvention
    {
        /// <summary>
        /// Gets the topic name for the given event type.
        /// </summary>
        /// <param name="eventType">The event type.</param>
        /// <returns>The topic/destination string.</returns>
        string GetTopic(Type eventType);

        /// <summary>
        /// Gets the topic name for the given event instance.
        /// Default implementation delegates to the type-based overload.
        /// </summary>
        /// <remarks>
        /// Not used for routing (SH-L128): DistributedEventBus publishes and subscribes by <see cref="GetTopic(Type)"/>,
        /// because a subscription has only the type, so an implementation that varies the topic per instance here does
        /// not change where an event is delivered.
        /// </remarks>
        /// <param name="event">The event instance.</param>
        /// <returns>The topic/destination string.</returns>
        string GetTopic(IEvent @event) => GetTopic(@event.GetType());
    }
}
