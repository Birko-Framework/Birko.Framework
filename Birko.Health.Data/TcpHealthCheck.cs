using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Health.Data;

/// <summary>
/// Health check for a generic TCP endpoint. Tests connectivity by performing a TCP connect
/// to the specified host and port.
/// </summary>
public sealed class TcpHealthCheck : IHealthCheck
{
    private readonly string _host;
    private readonly int _port;

    /// <summary>
    /// Creates a TCP health check.
    /// </summary>
    /// <param name="host">Target host.</param>
    /// <param name="port">Target port.</param>
    public TcpHealthCheck(string host, int port)
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
            var isConnected = client.Connected;

            sw.Stop();

            var data = new Dictionary<string, object>
            {
                ["host"] = _host,
                ["port"] = _port,
                ["latencyMs"] = Math.Round(sw.Elapsed.TotalMilliseconds, 2)
            };

            if (!isConnected)
            {
                return HealthCheckResult.Unhealthy($"TCP ({_host}:{_port}) not connected.", data: data);
            }

            if (sw.Elapsed.TotalMilliseconds > 2000)
            {
                return HealthCheckResult.Degraded($"TCP ({_host}:{_port}) responding slowly: {sw.Elapsed.TotalMilliseconds:F0}ms.", data: data);
            }

            return HealthCheckResult.Healthy($"TCP ({_host}:{_port}) OK ({sw.Elapsed.TotalMilliseconds:F0}ms).", data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"TCP ({_host}:{_port}) connection failed: {ex.Message}", ex);
        }
    }
}
