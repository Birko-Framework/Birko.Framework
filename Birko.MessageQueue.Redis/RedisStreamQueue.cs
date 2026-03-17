using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.MessageQueue.Serialization;
using Birko.Redis;

namespace Birko.MessageQueue.Redis
{
    /// <summary>
    /// Redis Streams message queue implementation using StackExchange.Redis.
    /// Uses Redis Streams (XADD/XREAD/XREADGROUP) for persistent, ordered messaging
    /// with consumer group support, acknowledgment (XACK), and automatic trimming (XTRIM).
    /// </summary>
    public class RedisStreamQueue : IMessageQueue
    {
        private readonly RedisConnectionManager _connectionManager;
        private readonly RedisStreamSettings _settings;
        private readonly bool _ownsConnection;
        private bool _disposed;

        public IMessageProducer Producer { get; }
        public IMessageConsumer Consumer { get; }
        public bool IsConnected => _connectionManager.IsConnected;

        /// <summary>
        /// Creates a new Redis Streams message queue with connection settings.
        /// </summary>
        /// <param name="settings">Redis stream connection settings.</param>
        /// <param name="serializer">Message serializer. Defaults to JsonMessageSerializer.</param>
        public RedisStreamQueue(RedisStreamSettings settings, IMessageSerializer? serializer = null)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _connectionManager = new RedisConnectionManager(settings);
            _ownsConnection = true;

            var ser = serializer ?? new JsonMessageSerializer();
            Producer = new RedisProducer(_connectionManager, ser, _settings);
            Consumer = new RedisConsumer(_connectionManager, ser, _settings);
        }

        /// <summary>
        /// Creates a new Redis Streams message queue from an existing connection manager.
        /// </summary>
        /// <param name="connectionManager">A pre-configured connection manager.</param>
        /// <param name="settings">Redis stream settings.</param>
        /// <param name="serializer">Message serializer. Defaults to JsonMessageSerializer.</param>
        public RedisStreamQueue(RedisConnectionManager connectionManager, RedisStreamSettings settings, IMessageSerializer? serializer = null)
        {
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _ownsConnection = false;

            var ser = serializer ?? new JsonMessageSerializer();
            Producer = new RedisProducer(_connectionManager, ser, _settings);
            Consumer = new RedisConsumer(_connectionManager, ser, _settings);
        }

        /// <summary>
        /// Gets the underlying connection manager for advanced scenarios.
        /// </summary>
        public RedisConnectionManager ConnectionManager => _connectionManager;

        public Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            // Force lazy connection creation by accessing the database
            _ = _connectionManager.GetDatabase();
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            // Redis connections are managed by the connection manager.
            // Disconnect is handled via Dispose.
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Consumer.Dispose();
            Producer.Dispose();

            if (_ownsConnection)
            {
                _connectionManager.Dispose();
            }
        }
    }
}
