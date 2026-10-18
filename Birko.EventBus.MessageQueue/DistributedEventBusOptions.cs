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
        /// How many times a failed event delivery is retried, and with what delay. Null (the default) leaves a failed
        /// delivery to the transport alone (TASK-668).
        /// </summary>
        /// <remarks>
        /// The framework's shared <see cref="global::Birko.RetryPolicy"/> (Birko.Contracts), named in full: the
        /// <c>Birko.MessageQueue.Retry</c> namespace imported here has a type of the same name, and the enclosing
        /// <c>Birko</c> namespace won the lookup silently (decided by the user 2026-10-18: this one).
        /// <para>
        /// A delivery is retried while its <c>x-delivery-count</c> is at most <see cref="global::Birko.RetryPolicy.MaxRetries"/>;
        /// after that it is dead-lettered (<see cref="DeadLetterOptions"/>) or, with none, dropped and reported through
        /// <see cref="DistributedEventBus.MessageDropped"/>. How the retry happens depends on the transport
        /// (<see cref="IRedeliveryCapability"/>): Redis with a consumer group redelivers the pending entry itself, after its
        /// <c>PendingRetryMilliseconds</c> (the policy's delay is not applied there); in-memory gets a re-send with the
        /// policy's delay. A transport that cannot count deliveries (MQTT) refuses the option at construction.
        /// </para>
        /// </remarks>
        public global::Birko.RetryPolicy? RetryPolicy { get; set; }

        /// <summary>
        /// Where an event goes once its retries are spent: <see cref="DeadLetterOptions.GetDeadLetterDestination"/> of the
        /// event's topic (<c>{topic}.dlq</c> by default). Null (the default) drops it with a
        /// <see cref="DistributedEventBus.MessageDropped"/> report instead. Set without a <see cref="RetryPolicy"/>,
        /// <see cref="global::Birko.RetryPolicy.Default"/> bounds the retries (TASK-668).
        /// </summary>
        public DeadLetterOptions? DeadLetterOptions { get; set; }

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
