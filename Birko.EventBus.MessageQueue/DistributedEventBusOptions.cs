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
        /// <remarks>
        /// CR-M187: retry is <b>delegated to the underlying <see cref="IMessageQueue"/> transport</b>, not
        /// applied by <see cref="DistributedEventBus"/>. The consumer dispatch callback faults when a
        /// handler throws (see the CR-H114 handling), and the transport re-delivers per its own configuration
        /// (<see cref="ConsumerOptions"/> / provider settings). This property is carried for provider adapters
        /// that choose to honor it; the bus itself does not read it, so setting it alone changes nothing.
        /// Configure retry at the transport to guarantee it takes effect.
        /// </remarks>
        public RetryPolicy RetryPolicy { get; set; } = RetryPolicy.Default;

        /// <summary>
        /// Dead letter queue options for events that exhaust retries.
        /// </summary>
        /// <remarks>
        /// CR-M187: like <see cref="RetryPolicy"/>, dead-lettering is delegated to the transport (a delivery
        /// whose callback keeps faulting is routed to the provider's DLQ). <see cref="DistributedEventBus"/>
        /// does not consume this property directly; configure the DLQ at the transport/provider.
        /// </remarks>
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
