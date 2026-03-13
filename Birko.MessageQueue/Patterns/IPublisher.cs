using System.Threading;
using System.Threading.Tasks;

namespace Birko.MessageQueue.Patterns
{
    /// <summary>
    /// Publishes messages to a topic (one-to-many).
    /// All subscribers to the topic receive the message.
    /// </summary>
    public interface IPublisher : IMessageProducer
    {
        /// <summary>
        /// Publishes a typed payload to a topic.
        /// </summary>
        Task PublishAsync<T>(string topic, T payload, MessageHeaders? headers = null, CancellationToken cancellationToken = default) where T : class;
    }
}
