using System.Threading;
using System.Threading.Tasks;

namespace Birko.EventBus
{
    /// <summary>
    /// Handles events of type <typeparamref name="TEvent"/>.
    /// Implement this interface and register via DI to receive events.
    /// </summary>
    /// <typeparam name="TEvent">The event type to handle.</typeparam>
    public interface IEventHandler<in TEvent> where TEvent : IEvent
    {
        /// <summary>
        /// Handles the event.
        /// </summary>
        /// <param name="event">The event instance.</param>
        /// <param name="context">Delivery context (correlation, tenant, metadata).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task HandleAsync(TEvent @event, EventContext context, CancellationToken cancellationToken = default);
    }
}
