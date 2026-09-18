using System;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.MessageQueue.InMemory
{
    /// <summary>
    /// Represents an active in-memory subscription.
    /// </summary>
    public class InMemorySubscription : ISubscription
    {
        private readonly InMemoryChannel _channel;
        private readonly Guid _subscriberId;

        public string Destination { get; }
        public bool IsActive { get; private set; } = true;

        internal InMemorySubscription(InMemoryChannel channel, string destination, Guid subscriberId)
        {
            _channel = channel;
            Destination = destination;
            _subscriberId = subscriberId;
        }

        public Task UnsubscribeAsync(CancellationToken cancellationToken = default)
        {
            if (IsActive)
            {
                _channel.RemoveSubscriber(Destination, _subscriberId);
                IsActive = false;
            }
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            if (IsActive)
            {
                _channel.RemoveSubscriber(Destination, _subscriberId);
                IsActive = false;
            }
        }
    }
}
