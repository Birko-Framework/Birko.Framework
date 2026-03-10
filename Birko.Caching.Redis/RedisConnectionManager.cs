using System;
using StackExchange.Redis;

namespace Birko.Caching.Redis;

/// <summary>
/// Manages a singleton ConnectionMultiplexer for Redis.
/// Thread-safe — ConnectionMultiplexer is designed to be shared.
/// </summary>
public sealed class RedisConnectionManager : IDisposable
{
    private readonly Lazy<ConnectionMultiplexer> _connection;
    private readonly RedisCacheOptions _options;
    private bool _disposed;

    public RedisConnectionManager(RedisCacheOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _connection = new Lazy<ConnectionMultiplexer>(() =>
            ConnectionMultiplexer.Connect(_options.ConnectionString));
    }

    public IDatabase GetDatabase() => _connection.Value.GetDatabase(_options.Database);
    public IServer GetServer() => _connection.Value.GetServer(_options.ConnectionString.Split(',')[0]);
    public bool IsConnected => _connection.IsValueCreated && _connection.Value.IsConnected;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_connection.IsValueCreated)
            _connection.Value.Dispose();
    }
}
