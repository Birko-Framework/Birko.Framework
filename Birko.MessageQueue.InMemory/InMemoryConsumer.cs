using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Birko.MessageQueue.Serialization;

namespace Birko.MessageQueue.InMemory
{
    /// <summary>
    /// In-memory message consumer. Subscribes to in-memory channels.
    /// </summary>
    public class InMemoryConsumer : IMessageConsumer
    {
        private readonly InMemoryChannel _channel;
        private readonly IMessageSerializer _serializer;
        private readonly ConcurrentDictionary<Guid, QueueMessage> _pendingAck = new();
        private bool _disposed;

        internal InMemoryConsumer(InMemoryChannel channel, IMessageSerializer serializer)
        {
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        }

        public Task<ISubscription> SubscribeAsync(string destination, Func<QueueMessage, CancellationToken, Task> handler, ConsumerOptions? options = null, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var opts = options ?? new ConsumerOptions();

            Func<QueueMessage, CancellationToken, Task> wrappedHandler = async (message, ct) =>
            {
                if (opts.AckMode == MessageAckMode.ManualAck)
                {
                    _pendingAck.TryAdd(message.Id, message);
                }

                try
                {
                    await handler(message, ct).ConfigureAwait(false);

                    if (opts.AckMode == MessageAckMode.AutoAck)
                    {
                        // Auto-acknowledged on success
                    }
                }
                catch
                {
                    if (opts.AckMode == MessageAckMode.ManualAck)
                    {
                        _pendingAck.TryRemove(message.Id, out _);
                    }
                    throw;
                }
            };

            var subscriberId = _channel.AddSubscriber(destination, wrappedHandler);
            ISubscription subscription = new InMemorySubscription(_channel, destination, subscriberId);
            return Task.FromResult(subscription);
        }

        public Task<ISubscription> SubscribeAsync<T>(string destination, IMessageHandler<T> handler, ConsumerOptions? options = null, CancellationToken cancellationToken = default) where T : class
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            return SubscribeAsync(destination, async (message, ct) =>
            {
                var payload = DeserializePayload<T>(message);
                if (payload != null)
                {
                    var context = new MessageContext(message, destination, this);
                    await handler.HandleAsync(payload, context, ct).ConfigureAwait(false);
                }
            }, options, cancellationToken);
        }

        public Task AcknowledgeAsync(Guid messageId, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _pendingAck.TryRemove(messageId, out _);
            return Task.CompletedTask;
        }

        public Task RejectAsync(Guid messageId, bool requeue = false, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_pendingAck.TryRemove(messageId, out var message) && requeue)
            {
                // Re-deliver by writing back to the channel
                // We need the destination, but for in-memory we can't know it here.
                // The message is simply discarded if not requeued.
            }

            return Task.CompletedTask;
        }

        private T? DeserializePayload<T>(QueueMessage message) where T : class
        {
            if (string.IsNullOrEmpty(message.Body))
            {
                return null;
            }

            return _serializer.Deserialize<T>(message.Body);
        }

        public void Dispose()
        {
            _disposed = true;
            _pendingAck.Clear();
        }
    }
}
