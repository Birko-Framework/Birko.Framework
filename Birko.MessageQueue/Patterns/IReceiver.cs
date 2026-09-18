using System;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.MessageQueue.Patterns
{
    /// <summary>
    /// Receives messages from a queue (point-to-point).
    /// Only one receiver processes each message.
    /// </summary>
    public interface IReceiver : IMessageConsumer
    {
        /// <summary>
        /// Receives the next message from the queue.
        /// Returns null if no message is available.
        /// </summary>
        Task<QueueMessage?> ReceiveAsync(string queue, TimeSpan? timeout = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Receives and deserializes the next message from the queue.
        /// Returns null if no message is available.
        /// </summary>
        Task<T?> ReceiveAsync<T>(string queue, TimeSpan? timeout = null, CancellationToken cancellationToken = default) where T : class;
    }
}
