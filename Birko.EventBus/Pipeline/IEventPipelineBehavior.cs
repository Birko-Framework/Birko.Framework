using System;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.EventBus.Pipeline
{
    /// <summary>
    /// Middleware behavior in the event handling pipeline.
    /// Wraps handler execution in a Russian-doll pattern (outer → inner → handler → inner → outer).
    /// </summary>
    public interface IEventPipelineBehavior
    {
        /// <summary>
        /// Executes this behavior around the next delegate in the pipeline.
        /// </summary>
        /// <param name="event">The event being handled.</param>
        /// <param name="context">The event delivery context.</param>
        /// <param name="next">The next behavior or handler in the pipeline.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task HandleAsync(IEvent @event, EventContext context, Func<Task> next, CancellationToken cancellationToken = default);
    }
}
