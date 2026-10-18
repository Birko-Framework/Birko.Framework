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
        private volatile bool _disposed; // SH-L116

        // SH-M111: the channel subscribers this consumer registered, removed on Dispose. They stayed on the shared
        // channel, so a disposed consumer kept receiving (and its handlers' Ack / Reject then threw ObjectDisposed).
        private readonly ConcurrentDictionary<Guid, string> _subscribers = new();

        /// <summary>
        /// Invoked when a message handler throws (SH-M109), so a failure is observable instead of discarded by the
        /// channel's dispatch loop. The in-memory transport does not redeliver a failed message by itself; under
        /// ManualAck it stays pending, so <see cref="RejectAsync"/> with requeue redelivers it. An exception thrown by
        /// the hook is swallowed.
        /// </summary>
        public Func<Exception, QueueMessage, Task>? OnHandlerError { get; set; }

        internal InMemoryConsumer(InMemoryChannel channel, IMessageSerializer serializer)
        {
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        }

        public Task<ISubscription> SubscribeAsync(string destination, Func<QueueMessage, CancellationToken, Task> handler, ConsumerOptions? options = null, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            // TASK-668: PrefetchCount is how many of this subscription's handlers run at once (the default 1 is one
            // message at a time, as before).
            var prefetch = TransportSupport.PrefetchOf(options);
            var opts = options ?? new ConsumerOptions();

            Func<QueueMessage, CancellationToken, Task> wrappedHandler = async (message, ct) =>
            {
                if (opts.AckMode == MessageAckMode.ManualAck)
                {
                    _pendingAck.TryAdd(message.Guid, (destination, message));
                }

                try
                {
                    await handler(message, ct).ConfigureAwait(false);

                    if (opts.AckMode == MessageAckMode.AutoAck)
                    {
                        // Auto-acknowledged on success
                    }
                }
                catch (Exception ex)
                {
                    // SH-M110: under ManualAck the entry stays pending, so the caller can still RejectAsync(requeue: true)
                    // it — that failure is exactly what requeue is for. It was removed here, so the requeue found nothing.
                    await InvokeHandlerErrorAsync(ex, message).ConfigureAwait(false);
                    throw;
                }
            };

            var subscriberId = _channel.AddSubscriber(destination, wrappedHandler, prefetch);
            _subscribers[subscriberId] = destination;
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
                // SH-L113: a requeued message is a redelivery; the x-delivery-count header counts it.
                var delivered = entry.Message.Headers.Custom.TryGetValue("x-delivery-count", out var seen) && int.TryParse(seen, out var n) ? n : 1;
                entry.Message.Headers.Custom["x-delivery-count"] = (delivered + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
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

        private async Task InvokeHandlerErrorAsync(Exception ex, QueueMessage message)
        {
            var hook = OnHandlerError;
            if (hook == null)
            {
                return;
            }

            try
            {
                await hook(ex, message).ConfigureAwait(false);
            }
            catch
            {
                // The error hook must never break dispatch.
            }
        }

        public void Dispose()
        {
            _disposed = true;
            foreach (var (subscriberId, destination) in _subscribers)
            {
                _channel.RemoveSubscriber(destination, subscriberId);
            }
            _subscribers.Clear();
            _pendingAck.Clear();
        }
    }
}
