using System.Threading;
using System.Threading.Tasks;

namespace Birko.MessageQueue.Patterns
{
    /// <summary>
    /// Sends messages to a queue (point-to-point).
    /// Only one receiver processes each message.
    /// </summary>
    public interface ISender : IMessageProducer
    {
        /// <summary>
        /// Sends a typed payload to a queue. Only one receiver will process it.
        /// </summary>
        Task SendToQueueAsync<T>(string queue, T payload, MessageHeaders? headers = null, CancellationToken cancellationToken = default) where T : class;
    }
}
