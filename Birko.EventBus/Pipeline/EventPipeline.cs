using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.EventBus.Pipeline
{
    /// <summary>
    /// Executes an ordered chain of <see cref="IEventPipelineBehavior"/> around a handler delegate.
    /// Behaviors are executed in registration order (first registered = outermost).
    /// </summary>
    public class EventPipeline
    {
        private readonly IReadOnlyList<IEventPipelineBehavior> _behaviors;

        public EventPipeline(IEnumerable<IEventPipelineBehavior> behaviors)
        {
            _behaviors = behaviors?.ToList() ?? [];
        }

        /// <summary>
        /// Runs the pipeline, wrapping the handler in all registered behaviors.
        /// </summary>
        /// <param name="event">The event being handled.</param>
        /// <param name="context">The event delivery context.</param>
        /// <param name="handler">The innermost handler delegate.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public Task ExecuteAsync(IEvent @event, EventContext context, Func<Task> handler, CancellationToken cancellationToken = default)
        {
            if (_behaviors.Count == 0)
            {
                return handler();
            }

            // Build the chain from inside out: handler → last behavior → ... → first behavior
            Func<Task> current = handler;
            for (int i = _behaviors.Count - 1; i >= 0; i--)
            {
                var behavior = _behaviors[i];
                var next = current;
                current = () => behavior.HandleAsync(@event, context, next, cancellationToken);
            }

            return current();
        }
    }
}
