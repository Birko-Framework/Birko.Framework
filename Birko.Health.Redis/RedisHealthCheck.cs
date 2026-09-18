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
        // CR-L269: StackExchange.Redis PingAsync has no CancellationToken overload, but honor an
        // already-cancelled token before doing any work. Placed before the try so the cancellation
        // propagates to HealthCheckRunner's timeout handling (which honors TimeoutStatus) rather than
        // being masked as a generic Unhealthy by the catch below (cf. CR-M191).
        ct.ThrowIfCancellationRequested();

        try
        {
            var connection = _connectionFactory();
            if (connection == null)
            {
                // CR-L270: the Func<IConnectionMultiplexer> overload can legally return null; report an
                // explicit reason instead of an obscure NullReferenceException-as-Unhealthy.
                return HealthCheckResult.Unhealthy("Redis connection factory returned null.");
            }

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
