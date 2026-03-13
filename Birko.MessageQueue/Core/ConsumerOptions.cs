namespace Birko.MessageQueue
{
    /// <summary>
    /// Options for message consumption.
    /// </summary>
    public class ConsumerOptions
    {
        /// <summary>
        /// Acknowledgment mode. Default is AutoAck.
        /// </summary>
        public MessageAckMode AckMode { get; set; } = MessageAckMode.AutoAck;

        /// <summary>
        /// Maximum number of unacknowledged messages (prefetch).
        /// 0 means unlimited. Default is 1.
        /// </summary>
        public int PrefetchCount { get; set; } = 1;

        /// <summary>
        /// Consumer group name for load balancing across multiple consumers.
        /// Null means no grouping (each consumer gets all messages).
        /// </summary>
        public string? GroupId { get; set; }

        /// <summary>
        /// Whether to process messages from the beginning of the queue/topic.
        /// When false, only new messages are received. Default is false.
        /// </summary>
        public bool FromBeginning { get; set; }
    }
}
