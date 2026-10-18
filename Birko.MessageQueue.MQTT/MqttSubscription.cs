using System;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.MessageQueue.Mqtt
{
    /// <summary>
    /// One handler's subscription to a topic filter. Releasing it removes that handler from the consumer (SH-M105: the
    /// handler stayed registered, so the next reconnect re-subscribed the filter and the released handler received again)
    /// and unsubscribes the filter at the broker only when no other subscription still needs it (SH-M106).
    /// </summary>
    public class MqttSubscription : ISubscription
    {
        private readonly MqttConsumer _consumer;
        private readonly Guid _subscriptionId;
        private int _released;

        public string Destination { get; }
        public bool IsActive => Volatile.Read(ref _released) == 0;

        internal MqttSubscription(MqttConsumer consumer, string destination, Guid subscriptionId)
        {
            _consumer = consumer;
            Destination = destination;
            _subscriptionId = subscriptionId;
        }

        public async Task UnsubscribeAsync(CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _released, 1) != 0)
            {
                return;
            }

            if (_consumer.RemoveHandler(Destination, _subscriptionId))
            {
                await _consumer.UnsubscribeFromBrokerAsync(Destination, cancellationToken).ConfigureAwait(false);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0)
            {
                return;
            }

            if (!_consumer.RemoveHandler(Destination, _subscriptionId))
            {
                return;
            }

            // Fire-and-forget broker unsubscribe on dispose; the handler is already gone locally.
            _ = Task.Run(async () =>
            {
                try
                {
                    await _consumer.UnsubscribeFromBrokerAsync(Destination).ConfigureAwait(false);
                }
                catch
                {
                    // Best-effort unsubscribe on dispose
                }
            });
        }
    }
}
