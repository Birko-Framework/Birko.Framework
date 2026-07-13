using System;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.MessageQueue.Redis
{
    /// <summary>
    /// Represents an active Redis Streams subscription.
    /// </summary>
    public class RedisSubscription : ISubscription
    {
        private readonly RedisConsumer _consumer;
        private readonly Guid _subscriptionId;
        private bool _unsubscribed;

        public string Destination { get; }

        /// <summary>
        /// True only while the subscription has not been unsubscribed/disposed AND its background poll
        /// loop is still registered with the consumer. CR-M207: a poll loop that terminates on an
        /// unexpected fault removes its registration, so this now correctly reports false instead of
        /// staying true while the subscription silently processes nothing.
        /// </summary>
        public bool IsActive => !_unsubscribed && _consumer.IsSubscriptionActive(_subscriptionId);

        internal RedisSubscription(RedisConsumer consumer, string destination, Guid subscriptionId)
        {
            _consumer = consumer;
            Destination = destination;
            _subscriptionId = subscriptionId;
        }

        public Task UnsubscribeAsync(CancellationToken cancellationToken = default)
        {
            if (!_unsubscribed)
            {
                _consumer.RemoveSubscription(_subscriptionId);
                _unsubscribed = true;
            }
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            if (!_unsubscribed)
            {
                _consumer.RemoveSubscription(_subscriptionId);
                _unsubscribed = true;
            }
        }
    }
}
