using System;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.MessageQueue
{
    /// <summary>
    /// Receives messages from a queue or topic.
    /// </summary>
    public interface IMessageConsumer : IDisposable
    {
        /// <summary>
        /// Subscribes to a destination and invokes the handler for each received message.
        /// Returns a subscription handle that can be disposed to unsubscribe.
        /// </summary>
        Task<ISubscription> SubscribeAsync(string destination, Func<QueueMessage, CancellationToken, Task> handler, ConsumerOptions? options = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Subscribes to a destination with a typed message handler.
        /// </summary>
        Task<ISubscription> SubscribeAsync<T>(string destination, IMessageHandler<T> handler, ConsumerOptions? options = null, CancellationToken cancellationToken = default) where T : class;

        /// <summary>
        /// Acknowledges a message (for manual acknowledgment mode).
        /// </summary>
        Task AcknowledgeAsync(Guid messageId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Rejects a message, optionally requeueing it.
        /// </summary>
        Task RejectAsync(Guid messageId, bool requeue = false, CancellationToken cancellationToken = default);
    }
}
