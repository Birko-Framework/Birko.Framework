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
        private int _released;
        public bool IsActive => Volatile.Read(ref _released) == 0;

        internal InMemorySubscription(InMemoryChannel channel, string destination, Guid subscriberId)
        {
            _channel = channel;
            Destination = destination;
            _subscriberId = subscriberId;
        }

        public Task UnsubscribeAsync(CancellationToken cancellationToken = default)
        {
            Release();
            return Task.CompletedTask;
        }

        public void Dispose() => Release();

        // SH-L115: one release, however many callers race to it (as InProcessEventSubscription since CR-L250).
        private void Release()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                _channel.RemoveSubscriber(Destination, _subscriberId);
            }
        }
    }
}
