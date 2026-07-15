using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.MessageQueue.Serialization;

namespace Birko.MessageQueue.InMemory
{
    /// <summary>
    /// In-memory message producer. Writes messages to in-memory channels.
    /// </summary>
    public class InMemoryProducer : IMessageProducer
    {
        private readonly InMemoryChannel _channel;
        private readonly IMessageSerializer _serializer;
        private bool _disposed;

        internal InMemoryProducer(InMemoryChannel channel, IMessageSerializer serializer)
        {
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        }

        /// <summary>
        /// Sends a message to the destination. When <see cref="QueueMessage.Delay"/> is set the enqueue is
        /// scheduled after the delay on a detached task and <b>delivery is best-effort</b> (CR-M201): a
        /// failure after the delay — cancellation, or the channel being completed/disposed — cannot be
        /// reported back through this already-completed call. Its fault is observed (not left unobserved)
        /// but is otherwise swallowed. For guaranteed delivery, send without a delay.
        /// </summary>
        public async Task SendAsync(string destination, QueueMessage message, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (message.Delay.HasValue)
            {
                // Observe the detached task's fault so a post-delay failure isn't raised as an
                // unobserved-task exception (best-effort, as documented above).
                _ = Task.Run(async () =>
                {
                    await Task.Delay(message.Delay.Value, cancellationToken).ConfigureAwait(false);
                    await _channel.WriteAsync(destination, message, cancellationToken).ConfigureAwait(false);
                }, cancellationToken)
                .ContinueWith(static t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
                return;
            }

            await _channel.WriteAsync(destination, message, cancellationToken).ConfigureAwait(false);
        }

        public async Task SendAsync<T>(string destination, T payload, MessageHeaders? headers = null, CancellationToken cancellationToken = default) where T : class
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var message = new QueueMessage
            {
                Body = _serializer.Serialize(payload),
                PayloadType = typeof(T).AssemblyQualifiedName,
                Headers = headers ?? new MessageHeaders()
            };

            // CR-L284: always stamp the serializer's content type — one assignment covers both the
            // new-headers and caller-supplied-headers cases (was an object-initializer set plus a
            // duplicate conditional set).
            message.Headers.ContentType = _serializer.ContentType;

            await SendAsync(destination, message, cancellationToken).ConfigureAwait(false);
        }

        public void Dispose()
        {
            _disposed = true;
        }
    }
}
