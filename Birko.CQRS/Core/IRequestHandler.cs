using System.Threading;
using System.Threading.Tasks;

namespace Birko.CQRS
{
    /// <summary>
    /// Handles a request and produces a result.
    /// This is the base handler interface used by the mediator pipeline.
    /// </summary>
    /// <typeparam name="TRequest">The type of request to handle.</typeparam>
    /// <typeparam name="TResult">The type of result produced.</typeparam>
    public interface IRequestHandler<in TRequest, TResult> where TRequest : IRequest<TResult>
    {
        /// <summary>
        /// Handles the request.
        /// </summary>
        /// <param name="request">The request to handle.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The result of handling the request.</returns>
        Task<TResult> HandleAsync(TRequest request, CancellationToken cancellationToken = default);
    }
}
