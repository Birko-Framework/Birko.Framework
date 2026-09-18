using System;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.MessageQueue
{
    /// <summary>
    /// Combined message queue interface providing both producer and consumer capabilities.
    /// Implementations wrap a specific message broker (RabbitMQ, Kafka, MQTT, etc.).
    /// </summary>
    public interface IMessageQueue : IDisposable
    {
        /// <summary>
        /// Gets the producer for sending messages.
        /// </summary>
        IMessageProducer Producer { get; }

        /// <summary>
        /// Gets the consumer for receiving messages.
        /// </summary>
        IMessageConsumer Consumer { get; }

        /// <summary>
        /// Gets a value indicating whether the queue is connected.
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Connects to the message broker.
        /// </summary>
        Task ConnectAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Disconnects from the message broker.
        /// </summary>
        Task DisconnectAsync(CancellationToken cancellationToken = default);
    }
}
