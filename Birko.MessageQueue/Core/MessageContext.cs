using System;

namespace Birko.MessageQueue
{
    /// <summary>
    /// Runtime context available to message handlers during processing.
    /// </summary>
    public class MessageContext
    {
        /// <summary>
        /// The original queue message.
        /// </summary>
        public QueueMessage Message { get; }

        /// <summary>
        /// The destination (queue/topic) the message was received from.
        /// </summary>
        public string Destination { get; }

        /// <summary>
        /// The consumer processing this message.
        /// </summary>
        public IMessageConsumer Consumer { get; }

        /// <summary>
        /// Number of times this message has been delivered (1 = first delivery).
        /// </summary>
        public int DeliveryCount { get; set; } = 1;

        /// <summary>
        /// When the message was received by the consumer.
        /// </summary>
        public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;

        public MessageContext(QueueMessage message, string destination, IMessageConsumer consumer)
        {
            Message = message ?? throw new ArgumentNullException(nameof(message));
            Destination = destination ?? throw new ArgumentNullException(nameof(destination));
            Consumer = consumer ?? throw new ArgumentNullException(nameof(consumer));
        }
    }
}
