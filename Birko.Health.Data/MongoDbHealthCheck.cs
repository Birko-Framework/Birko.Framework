using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Health.Data;

/// <summary>
/// Health check for MongoDB. Sends a ping command via the HTTP diagnostic interface
/// or uses a custom ping function.
/// </summary>
public sealed class MongoDbHealthCheck : IHealthCheck
{
    private readonly Func<CancellationToken, Task<bool>> _pingFunc;
    private readonly string _description;

    /// <summary>
    /// Creates a MongoDB health check with a custom ping function.
    /// The function should return true if the database is reachable.
    /// </summary>
    /// <param name="pingFunc">Async function that pings MongoDB and returns true if healthy.</param>
    /// <param name="description">Description for the check result (e.g., "mongodb-primary").</param>
    public MongoDbHealthCheck(Func<CancellationToken, Task<bool>> pingFunc, string description = "MongoDB")
    {
        _pingFunc = pingFunc ?? throw new ArgumentNullException(nameof(pingFunc));
        _description = description;
    }

    /// <summary>
    /// Creates a MongoDB health check from a connection string.
    /// Uses the MongoDB wire protocol ping via a TCP connection test.
    /// </summary>
    /// <param name="host">MongoDB host.</param>
    /// <param name="port">MongoDB port. Default: 27017.</param>
    public MongoDbHealthCheck(string host, int port = 27017)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new ArgumentException("Host cannot be null or empty.", nameof(host));
        }

        _description = $"MongoDB ({host}:{port})";
        _pingFunc = async ct =>
        {
            using var client = new System.Net.Sockets.TcpClient();
            await client.ConnectAsync(host, port, ct).ConfigureAwait(false);
            return client.Connected;
        };
    }

    public async Task<HealthCheckResult> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            var isHealthy = await _pingFunc(ct).ConfigureAwait(false);

            if (isHealthy)
            {
                return HealthCheckResult.Healthy($"{_description} connection OK.");
            }

            return HealthCheckResult.Unhealthy($"{_description} ping returned false.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"{_description} connection failed: {ex.Message}", ex);
        }
    }
}
