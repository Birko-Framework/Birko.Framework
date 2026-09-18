using System.Threading;
using System.Threading.Tasks;

namespace Birko.CQRS
{
    /// <summary>
    /// Dispatches requests to their corresponding handlers through the pipeline.
    /// </summary>
    public interface IMediator
    {
        /// <summary>
        /// Sends a request through the pipeline and returns the result.
        /// </summary>
        /// <typeparam name="TResult">The type of result.</typeparam>
        /// <param name="request">The request to send.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The result from the handler.</returns>
        Task<TResult> SendAsync<TResult>(IRequest<TResult> request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends a command that returns no result.
        /// </summary>
        /// <param name="command">The command to send.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task SendAsync(ICommand command, CancellationToken cancellationToken = default);
    }
}
