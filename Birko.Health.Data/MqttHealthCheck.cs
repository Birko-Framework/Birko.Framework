using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Health.Data;

/// <summary>
/// Health check for an MQTT broker. Tests TCP connectivity to the broker endpoint.
/// </summary>
public sealed class MqttHealthCheck : IHealthCheck
{
    private readonly string _host;
    private readonly int _port;
    private readonly Func<CancellationToken, Task<bool>>? _customPing;
    private readonly string _description;

    /// <summary>
    /// Creates an MQTT health check using TCP connectivity.
    /// </summary>
    /// <param name="host">MQTT broker host.</param>
    /// <param name="port">MQTT broker port. Default: 1883.</param>
    public MqttHealthCheck(string host, int port = 1883)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new ArgumentException("Host cannot be null or empty.", nameof(host));
        }

        _host = host;
        _port = port;
        _description = $"MQTT ({host}:{port})";
    }

    /// <summary>
    /// Creates an MQTT health check with a custom ping function.
    /// Use this when you have access to the MqttMessageQueue instance and can check IsConnected.
    /// </summary>
    /// <param name="pingFunc">Async function that checks broker connectivity. Returns true if healthy.</param>
    /// <param name="description">Description for the check result.</param>
    public MqttHealthCheck(Func<CancellationToken, Task<bool>> pingFunc, string description = "MQTT")
    {
        _customPing = pingFunc ?? throw new ArgumentNullException(nameof(pingFunc));
        _host = string.Empty;
        _description = description;
    }

    public async Task<HealthCheckResult> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            var sw = Stopwatch.StartNew();
            bool isConnected;

            if (_customPing != null)
            {
                isConnected = await _customPing(ct).ConfigureAwait(false);
            }
            else
            {
                using var client = new TcpClient();
                await client.ConnectAsync(_host, _port, ct).ConfigureAwait(false);
                isConnected = client.Connected;
            }

            sw.Stop();

            var data = new Dictionary<string, object>
            {
                ["latencyMs"] = Math.Round(sw.Elapsed.TotalMilliseconds, 2)
            };

            if (!string.IsNullOrEmpty(_host))
            {
                data["host"] = _host;
                data["port"] = _port;
            }

            if (!isConnected)
            {
                return HealthCheckResult.Unhealthy($"{_description} not connected.", data: data);
            }

            if (sw.Elapsed.TotalMilliseconds > 2000)
            {
                return HealthCheckResult.Degraded($"{_description} responding slowly: {sw.Elapsed.TotalMilliseconds:F0}ms.", data: data);
            }

            return HealthCheckResult.Healthy($"{_description} OK ({sw.Elapsed.TotalMilliseconds:F0}ms).", data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"{_description} connection failed: {ex.Message}", ex);
        }
    }
}
