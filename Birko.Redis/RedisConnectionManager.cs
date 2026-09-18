using System;
using System.Threading;
using StackExchange.Redis;

namespace Birko.Redis
{
    /// <summary>
    /// Manages a singleton ConnectionMultiplexer for Redis.
    /// Thread-safe — ConnectionMultiplexer is designed to be shared across an application.
    /// </summary>
    public sealed class RedisConnectionManager : IDisposable
    {
        private readonly Lazy<ConnectionMultiplexer> _connection;
        private readonly string _connectionString;
        private readonly int _database;
        private bool _disposed;

        /// <summary>
        /// Creates a connection manager from RedisSettings.
        /// </summary>
        /// <param name="settings">The Redis connection settings.</param>
        public RedisConnectionManager(RedisSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            _connectionString = settings.GetConnectionString();
            _database = settings.Database;
            _connection = new Lazy<ConnectionMultiplexer>(
                () => ConnectionMultiplexer.Connect(_connectionString),
                // CR-M231: PublicationOnly does NOT cache the factory's exception, so a transient
                // connect failure at first access can be retried on the next access rather than
                // permanently poisoning this long-lived singleton (the default ExecutionAndPublication
                // caches the exception forever).
                LazyThreadSafetyMode.PublicationOnly);
        }

        /// <summary>
        /// Creates a connection manager from a raw connection string.
        /// </summary>
        /// <param name="connectionString">A StackExchange.Redis connection string.</param>
        /// <param name="database">The database index (0-15).</param>
        public RedisConnectionManager(string connectionString, int database = 0)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _database = database;
            _connection = new Lazy<ConnectionMultiplexer>(
                () => ConnectionMultiplexer.Connect(_connectionString),
                LazyThreadSafetyMode.PublicationOnly); // CR-M231: don't cache a transient connect failure
        }

        /// <summary>
        /// Gets the Redis database instance.
        /// </summary>
        public IDatabase GetDatabase() => _connection.Value.GetDatabase(_database);

        /// <summary>
        /// Gets the Redis server for administrative operations (e.g., SCAN).
        /// </summary>
        public IServer GetServer()
        {
            // CR-L330: resolve the endpoint from the live multiplexer rather than re-parsing
            // _connectionString.Split(',')[0] — that assumed the first comma-token is always host:port,
            // which breaks for option-first tokens (e.g. "abortConnect=false,host:port") or raw strings,
            // and silently dropped all but the first endpoint of a cluster.
            var multiplexer = _connection.Value;
            var endpoints = multiplexer.GetEndPoints();
            return multiplexer.GetServer(endpoints[0]);
        }

        /// <summary>
        /// Whether the connection is established and healthy.
        /// </summary>
        public bool IsConnected => _connection.IsValueCreated && _connection.Value.IsConnected;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_connection.IsValueCreated)
                _connection.Value.Dispose();
        }
    }
}
