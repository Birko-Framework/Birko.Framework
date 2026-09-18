using System;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.EventBus
{
    /// <summary>
    /// Publishes and subscribes to strongly-typed events.
    /// </summary>
    public interface IEventBus : IDisposable
    {
        /// <summary>
        /// Publishes an event to all registered handlers.
        /// </summary>
        /// <typeparam name="TEvent">The event type.</typeparam>
        /// <param name="event">The event instance.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : IEvent;

        /// <summary>
        /// Subscribes a handler for events of type <typeparamref name="TEvent"/>.
        /// Returns a subscription that can be disposed to unsubscribe.
        /// </summary>
        /// <typeparam name="TEvent">The event type.</typeparam>
        /// <param name="handler">The event handler.</param>
        /// <returns>A subscription handle. Dispose to unsubscribe.</returns>
        IEventSubscription Subscribe<TEvent>(IEventHandler<TEvent> handler) where TEvent : IEvent;
    }
}
