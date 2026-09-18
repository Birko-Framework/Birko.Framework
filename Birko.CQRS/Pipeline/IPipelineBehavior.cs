using System;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.CQRS.Pipeline
{
    /// <summary>
    /// Middleware behavior in the request handling pipeline.
    /// Wraps handler execution in a Russian-doll pattern (outer -> inner -> handler -> inner -> outer).
    /// </summary>
    /// <typeparam name="TRequest">The type of request.</typeparam>
    /// <typeparam name="TResult">The type of result.</typeparam>
    public interface IPipelineBehavior<in TRequest, TResult> where TRequest : IRequest<TResult>
    {
        /// <summary>
        /// Executes this behavior around the next delegate in the pipeline.
        /// </summary>
        /// <param name="request">The request being handled.</param>
        /// <param name="next">The next behavior or handler in the pipeline.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The result from the pipeline.</returns>
        Task<TResult> HandleAsync(TRequest request, Func<CancellationToken, Task<TResult>> next, CancellationToken cancellationToken = default);
    }
}
