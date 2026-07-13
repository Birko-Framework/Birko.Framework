using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.MessageQueue.Serialization;
using Birko.Redis;
using StackExchange.Redis;

namespace Birko.MessageQueue.Redis
{
    /// <summary>
    /// Redis Streams message producer. Uses XADD to append messages to streams.
    /// </summary>
    public class RedisProducer : IMessageProducer
    {
        private readonly RedisConnectionManager _connectionManager;
        private readonly IMessageSerializer _serializer;
        private readonly RedisStreamSettings _settings;
        private bool _disposed;

        internal RedisProducer(RedisConnectionManager connectionManager, IMessageSerializer serializer, RedisStreamSettings settings)
        {
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public async Task SendAsync(string destination, QueueMessage message, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested(); // CR-M206: StackExchange.Redis has no per-call token; gate on entry

            if (string.IsNullOrEmpty(destination))
            {
                throw new ArgumentException("Destination cannot be null or empty.", nameof(destination));
            }

            var db = _connectionManager.GetDatabase();
            var streamKey = _settings.GetStreamKey(destination);

            var serializedMessage = _serializer.Serialize(message);

            var entries = new NameValueEntry[]
            {
                new("id", message.Id.ToString()),
                new("body", message.Body),
                new("payload_type", message.PayloadType ?? string.Empty),
                new("headers", _serializer.Serialize(message.Headers)),
                new("created_at", message.CreatedAt.ToUnixTimeMilliseconds().ToString()),
                new("priority", message.Priority.ToString()),
                new("message", serializedMessage)
            };

            if (message.TimeToLive.HasValue)
            {
                // Store TTL as a field; consumers check expiry
                entries = AppendEntry(entries, new NameValueEntry("ttl_ms", ((long)message.TimeToLive.Value.TotalMilliseconds).ToString()));
            }

            if (_settings.MaxStreamLength.HasValue)
            {
                await db.StreamAddAsync(streamKey, entries, maxLength: _settings.MaxStreamLength.Value, useApproximateMaxLength: true).ConfigureAwait(false);
            }
            else
            {
                await db.StreamAddAsync(streamKey, entries).ConfigureAwait(false);
            }
        }

        public async Task SendAsync<T>(string destination, T payload, MessageHeaders? headers = null, CancellationToken cancellationToken = default) where T : class
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var body = _serializer.Serialize(payload);
            var message = new QueueMessage
            {
                Body = body,
                PayloadType = typeof(T).AssemblyQualifiedName,
                Headers = headers ?? new MessageHeaders { ContentType = _serializer.ContentType }
            };

            if (headers != null)
            {
                message.Headers.ContentType = _serializer.ContentType;
            }

            await SendAsync(destination, message, cancellationToken).ConfigureAwait(false);
        }

        private static NameValueEntry[] AppendEntry(NameValueEntry[] existing, NameValueEntry entry)
        {
            var result = new NameValueEntry[existing.Length + 1];
            existing.CopyTo(result, 0);
            result[existing.Length] = entry;
            return result;
        }

        public void Dispose()
        {
            _disposed = true;
        }
    }
}
