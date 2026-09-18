using System;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.MessageQueue.Patterns
{
    /// <summary>
    /// Subscribes to messages from a topic (one-to-many).
    /// </summary>
    public interface ISubscriber : IMessageConsumer
    {
        /// <summary>
        /// Subscribes to a topic with a typed handler.
        /// </summary>
        Task<ISubscription> SubscribeAsync<T>(string topic, Func<T, MessageContext, CancellationToken, Task> handler, ConsumerOptions? options = null, CancellationToken cancellationToken = default) where T : class;
    }
}
