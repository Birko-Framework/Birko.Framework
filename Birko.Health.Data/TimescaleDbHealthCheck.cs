using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Health.Data;

/// <summary>
/// Health check for TimescaleDB. Performs a TCP connection test to the PostgreSQL port.
/// TimescaleDB runs as a PostgreSQL extension, so connectivity is checked via TCP.
/// For deeper checks, use SqlHealthCheck with a TimescaleDB connection.
/// </summary>
public sealed class TimescaleDbHealthCheck : IHealthCheck
{
    private readonly string _host;
    private readonly int _port;

    /// <summary>
    /// Creates a TimescaleDB health check.
    /// </summary>
    /// <param name="host">TimescaleDB/PostgreSQL host.</param>
    /// <param name="port">TimescaleDB/PostgreSQL port (default 5432).</param>
    public TimescaleDbHealthCheck(string host, int port = 5432)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new ArgumentException("Host cannot be null or empty.", nameof(host));
        }
        if (port < 1 || port > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), "Port must be between 1 and 65535.");
        }

        _host = host;
        _port = port;
    }

    public async Task<HealthCheckResult> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            var sw = Stopwatch.StartNew();
            using var client = new TcpClient();
            await client.ConnectAsync(_host, _port, ct).ConfigureAwait(false);
            sw.Stop();

            var data = new Dictionary<string, object>
            {
                ["host"] = _host,
                ["port"] = _port,
                ["latencyMs"] = sw.ElapsedMilliseconds
            };

            if (sw.ElapsedMilliseconds > 2000)
            {
                return HealthCheckResult.Degraded($"TimescaleDB reachable but slow ({sw.ElapsedMilliseconds}ms).", data: data);
            }

            return HealthCheckResult.Healthy($"TimescaleDB OK ({_host}:{_port}).", data: data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"TimescaleDB connection failed: {ex.Message}", ex);
        }
    }
}
