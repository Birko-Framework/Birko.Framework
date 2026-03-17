using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace Birko.Health.Redis;

/// <summary>
/// Health check for Redis. Sends a PING command.
/// </summary>
public sealed class RedisHealthCheck : IHealthCheck
{
    private readonly Func<IConnectionMultiplexer> _connectionFactory;

    /// <summary>
    /// Creates a Redis health check from a connection multiplexer factory.
    /// </summary>
    public RedisHealthCheck(Func<IConnectionMultiplexer> connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    /// <summary>
    /// Creates a Redis health check from an existing connection multiplexer.
    /// </summary>
    public RedisHealthCheck(IConnectionMultiplexer connection)
    {
        if (connection == null)
        {
            throw new ArgumentNullException(nameof(connection));
        }
        _connectionFactory = () => connection;
    }

    public async Task<HealthCheckResult> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            var connection = _connectionFactory();
            var db = connection.GetDatabase();
            var latency = await db.PingAsync().ConfigureAwait(false);

            var data = new Dictionary<string, object>
            {
                ["latencyMs"] = Math.Round(latency.TotalMilliseconds, 2),
                ["isConnected"] = connection.IsConnected
            };

            if (!connection.IsConnected)
            {
                return HealthCheckResult.Unhealthy("Redis is not connected.", data: data);
            }

            if (latency.TotalMilliseconds > 100)
            {
                return HealthCheckResult.Degraded(
                    $"Redis responding slowly: {latency.TotalMilliseconds:F1}ms.", data: data);
            }

            return HealthCheckResult.Healthy(
                $"Redis OK ({latency.TotalMilliseconds:F1}ms).", data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"Redis connection failed: {ex.Message}", ex);
        }
    }
}
