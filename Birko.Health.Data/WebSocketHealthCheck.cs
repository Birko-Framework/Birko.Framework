using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Health.Data;

/// <summary>
/// Health check for a WebSocket endpoint. Tests connectivity by performing a WebSocket
/// handshake (HTTP Upgrade) and then closing the connection gracefully.
/// </summary>
public sealed class WebSocketHealthCheck : IHealthCheck
{
    private readonly Uri _uri;
    private readonly Func<CancellationToken, Task<bool>>? _customPing;
    private readonly string _description;

    /// <summary>
    /// Creates a WebSocket health check using a URI.
    /// </summary>
    /// <param name="uri">WebSocket endpoint URI (ws:// or wss://).</param>
    public WebSocketHealthCheck(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            throw new ArgumentException("URI cannot be null or empty.", nameof(uri));
        }

        _uri = new Uri(uri);
        _description = $"WebSocket ({_uri.Host}:{_uri.Port})";
    }

    /// <summary>
    /// Creates a WebSocket health check with a custom ping function.
    /// Use this when you have access to an existing WebSocket connection or manager.
    /// </summary>
    /// <param name="pingFunc">Async function that checks WebSocket connectivity. Returns true if healthy.</param>
    /// <param name="description">Description for the check result.</param>
    public WebSocketHealthCheck(Func<CancellationToken, Task<bool>> pingFunc, string description = "WebSocket")
    {
        _customPing = pingFunc ?? throw new ArgumentNullException(nameof(pingFunc));
        _uri = null!;
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
                using var client = new ClientWebSocket();
                await client.ConnectAsync(_uri, ct).ConfigureAwait(false);
                isConnected = client.State == WebSocketState.Open;
                if (isConnected)
                {
                    await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Health check", ct).ConfigureAwait(false);
                }
            }

            sw.Stop();

            var data = new Dictionary<string, object>
            {
                ["latencyMs"] = Math.Round(sw.Elapsed.TotalMilliseconds, 2)
            };

            if (_uri != null)
            {
                data["uri"] = _uri.ToString();
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
