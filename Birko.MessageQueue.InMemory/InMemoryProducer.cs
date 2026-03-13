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

        public async Task SendAsync(string destination, QueueMessage message, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (message.Delay.HasValue)
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(message.Delay.Value, cancellationToken).ConfigureAwait(false);
                    await _channel.WriteAsync(destination, message, cancellationToken).ConfigureAwait(false);
                }, cancellationToken);
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
                Headers = headers ?? new MessageHeaders { ContentType = _serializer.ContentType }
            };

            if (headers != null)
            {
                message.Headers.ContentType = _serializer.ContentType;
            }

            await SendAsync(destination, message, cancellationToken).ConfigureAwait(false);
        }

        public void Dispose()
        {
            _disposed = true;
        }
    }
}
