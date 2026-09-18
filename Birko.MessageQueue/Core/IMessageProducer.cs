using System;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.MessageQueue
{
    /// <summary>
    /// Sends messages to a queue or topic.
    /// </summary>
    public interface IMessageProducer : IDisposable
    {
        /// <summary>
        /// Sends a message to the specified destination (queue or topic).
        /// </summary>
        Task SendAsync(string destination, QueueMessage message, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends a typed payload to the specified destination.
        /// The payload is serialized and wrapped in a QueueMessage.
        /// </summary>
        Task SendAsync<T>(string destination, T payload, MessageHeaders? headers = null, CancellationToken cancellationToken = default) where T : class;
    }
}
