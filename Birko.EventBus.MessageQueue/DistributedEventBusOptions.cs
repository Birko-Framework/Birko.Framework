using Birko.EventBus.Routing;
using Birko.MessageQueue;
using Birko.MessageQueue.Retry;
using Birko.MessageQueue.Serialization;

namespace Birko.EventBus.MessageQueue
{
    /// <summary>
    /// Options for <see cref="DistributedEventBus"/>.
    /// </summary>
    public class DistributedEventBusOptions
    {
        /// <summary>
        /// Topic naming convention for mapping event types to queue destinations.
        /// Default is <see cref="DefaultTopicConvention"/>.
        /// </summary>
        public ITopicConvention TopicConvention { get; set; } = new DefaultTopicConvention();

        /// <summary>
        /// Serializer for event envelope payloads. Default is JSON.
        /// </summary>
        public IMessageSerializer? Serializer { get; set; }

        /// <summary>
        /// Retry policy for failed event deliveries.
        /// </summary>
        public RetryPolicy RetryPolicy { get; set; } = RetryPolicy.Default;

        /// <summary>
        /// Dead letter queue options for events that exhaust retries.
        /// </summary>
        public DeadLetterOptions DeadLetterOptions { get; set; } = new();

        /// <summary>
        /// Consumer options for subscriptions (ack mode, prefetch, consumer group).
        /// </summary>
        public ConsumerOptions? ConsumerOptions { get; set; }

        /// <summary>
        /// Whether to automatically scan DI for IEventHandler&lt;T&gt; and create subscriptions on startup.
        /// Default is true.
        /// </summary>
        public bool AutoSubscribe { get; set; } = true;
    }
}
