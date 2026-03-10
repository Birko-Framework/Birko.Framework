using Birko.Data.EventSourcing.Events;
using System;

namespace Birko.Data.EventSourcing.Models
{
    /// <summary>
    /// Interface for entities that support event sourcing.
    /// </summary>
    public interface IEventSourced
    {
        /// <summary>
        /// The version of the aggregate, used for optimistic concurrency.
        /// </summary>
        long Version { get; set; }

        /// <summary>
        /// Applies an event to this aggregate, updating its state.
        /// </summary>
        /// <param name="event">The event to apply.</param>
        void ApplyEvent(IEvent @event);

        /// <summary>
        /// Gets uncommitted events that need to be persisted.
        /// </summary>
        IEvent[] GetUncommittedEvents();

        /// <summary>
        /// Marks events as committed, clearing the uncommitted events.
        /// </summary>
        void MarkEventsAsCommitted();

        /// <summary>
        /// Loads an aggregate from a stream of events.
        /// </summary>
        /// <param name="events">The events to apply.</param>
        void LoadFromEvents(System.Collections.Generic.IEnumerable<IEvent> events);
    }

    /// <summary>
    /// Base class for event-sourced aggregates.
    /// </summary>
    public abstract class EventSourcedAggregate : IEventSourced
    {
        private readonly System.Collections.Generic.List<IEvent> _uncommittedEvents = new();

        /// <inheritdoc />
        public long Version { get; set; }

        /// <inheritdoc />
        public virtual void ApplyEvent(IEvent @event)
        {
            Version = @event.Version;
            _uncommittedEvents.Add(@event);
        }

        /// <inheritdoc />
        public virtual IEvent[] GetUncommittedEvents()
        {
            return _uncommittedEvents.ToArray();
        }

        /// <inheritdoc />
        public virtual void MarkEventsAsCommitted()
        {
            _uncommittedEvents.Clear();
        }

        /// <summary>
        /// Loads an aggregate from a stream of events.
        /// </summary>
        /// <param name="events">The events to apply.</param>
        public void LoadFromEvents(System.Collections.Generic.IEnumerable<IEvent> events)
        {
            foreach (var @event in events)
            {
                ApplyEvent(@event);
            }
            MarkEventsAsCommitted();
        }
    }
}
