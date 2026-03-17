using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Health.Data;

/// <summary>
/// Health check for InfluxDB. Calls the /ping endpoint.
/// </summary>
public sealed class InfluxDbHealthCheck : IHealthCheck
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    /// <summary>
    /// Creates an InfluxDB health check.
    /// </summary>
    /// <param name="baseUrl">InfluxDB base URL (e.g., "http://localhost:8086").</param>
    /// <param name="httpClient">Optional HttpClient instance. If null, a new one is created.</param>
    public InfluxDbHealthCheck(string baseUrl, HttpClient? httpClient = null)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new ArgumentException("Base URL cannot be null or empty.", nameof(baseUrl));
        }

        _baseUrl = baseUrl.TrimEnd('/');
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<HealthCheckResult> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var response = await _httpClient.GetAsync($"{_baseUrl}/ping", ct).ConfigureAwait(false);
            sw.Stop();

            var data = new Dictionary<string, object>
            {
                ["url"] = _baseUrl,
                ["latencyMs"] = Math.Round(sw.Elapsed.TotalMilliseconds, 2),
                ["statusCode"] = (int)response.StatusCode
            };

            if (!response.IsSuccessStatusCode)
            {
                return HealthCheckResult.Unhealthy($"InfluxDB returned {(int)response.StatusCode}.", data: data);
            }

            if (sw.Elapsed.TotalMilliseconds > 2000)
            {
                return HealthCheckResult.Degraded($"InfluxDB responding slowly: {sw.Elapsed.TotalMilliseconds:F0}ms.", data: data);
            }

            return HealthCheckResult.Healthy($"InfluxDB OK ({sw.Elapsed.TotalMilliseconds:F0}ms).", data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"InfluxDB connection failed: {ex.Message}", ex);
        }
    }
}
