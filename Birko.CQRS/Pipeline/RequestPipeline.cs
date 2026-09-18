using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.CQRS.Pipeline
{
    /// <summary>
    /// Executes an ordered chain of <see cref="IPipelineBehavior{TRequest, TResult}"/> around a handler delegate.
    /// Behaviors are executed in registration order (first registered = outermost).
    /// </summary>
    public class RequestPipeline<TRequest, TResult> where TRequest : IRequest<TResult>
    {
        private readonly IReadOnlyList<IPipelineBehavior<TRequest, TResult>> _behaviors;

        public RequestPipeline(IEnumerable<IPipelineBehavior<TRequest, TResult>> behaviors)
        {
            _behaviors = behaviors?.ToList() ?? [];
        }

        /// <summary>
        /// Runs the pipeline, wrapping the handler in all registered behaviors.
        /// </summary>
        /// <param name="request">The request being handled.</param>
        /// <param name="handler">The innermost handler delegate.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The result from the pipeline.</returns>
        public Task<TResult> ExecuteAsync(TRequest request, Func<CancellationToken, Task<TResult>> handler, CancellationToken cancellationToken = default)
        {
            if (_behaviors.Count == 0)
            {
                return handler(cancellationToken);
            }

            // Build the chain from inside out: handler -> last behavior -> ... -> first behavior
            Func<CancellationToken, Task<TResult>> current = handler;
            for (int i = _behaviors.Count - 1; i >= 0; i--)
            {
                var behavior = _behaviors[i];
                var next = current;
                current = ct => behavior.HandleAsync(request, next, ct);
            }

            return current(cancellationToken);
        }
    }
}
