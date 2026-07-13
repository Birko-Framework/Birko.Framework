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
        // CR-M202: track the originating destination alongside the message so RejectAsync(requeue:true)
        // can write it back to the right channel instead of silently discarding it.
        private readonly ConcurrentDictionary<Guid, (string Destination, QueueMessage Message)> _pendingAck = new();
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
                    _pendingAck.TryAdd(message.Id, (destination, message));
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

        public async Task RejectAsync(Guid messageId, bool requeue = false, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_pendingAck.TryRemove(messageId, out var entry) && requeue)
            {
                // CR-M202: re-deliver by writing the message back to its originating channel (the
                // destination is now tracked with the pending entry). Previously requeue:true was a
                // silent no-op — identical to discard — causing message loss for poison-message handling.
                await _channel.WriteAsync(entry.Destination, entry.Message, cancellationToken).ConfigureAwait(false);
            }
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
