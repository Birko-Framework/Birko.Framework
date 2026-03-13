using System.Threading;
using System.Threading.Tasks;

namespace Birko.MessageQueue
{
    /// <summary>
    /// Handles a received message of a specific type.
    /// </summary>
    public interface IMessageHandler<T> where T : class
    {
        /// <summary>
        /// Handles the deserialized message payload.
        /// </summary>
        Task HandleAsync(T message, MessageContext context, CancellationToken cancellationToken = default);
    }
}
