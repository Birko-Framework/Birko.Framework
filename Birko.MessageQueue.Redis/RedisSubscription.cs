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

        public string Destination { get; }
        public bool IsActive { get; private set; } = true;

        internal RedisSubscription(RedisConsumer consumer, string destination, Guid subscriptionId)
        {
            _consumer = consumer;
            Destination = destination;
            _subscriptionId = subscriptionId;
        }

        public Task UnsubscribeAsync(CancellationToken cancellationToken = default)
        {
            if (IsActive)
            {
                _consumer.RemoveSubscription(_subscriptionId);
                IsActive = false;
            }
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            if (IsActive)
            {
                _consumer.RemoveSubscription(_subscriptionId);
                IsActive = false;
            }
        }
    }
}
