using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Health.Data;

/// <summary>
/// Health check for HashiCorp Vault. Calls the /v1/sys/health endpoint.
/// </summary>
public sealed class VaultHealthCheck : IHealthCheck
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    /// <summary>
    /// Creates a Vault health check.
    /// </summary>
    /// <param name="baseUrl">Vault server URL (e.g., "http://localhost:8200").</param>
    /// <param name="httpClient">Optional HttpClient instance. If null, a new one is created.</param>
    public VaultHealthCheck(string baseUrl, HttpClient? httpClient = null)
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
            var response = await _httpClient.GetAsync($"{_baseUrl}/v1/sys/health", ct).ConfigureAwait(false);
            sw.Stop();

            var data = new Dictionary<string, object>
            {
                ["url"] = _baseUrl,
                ["latencyMs"] = Math.Round(sw.Elapsed.TotalMilliseconds, 2),
                ["statusCode"] = (int)response.StatusCode
            };

            // Vault /v1/sys/health returns:
            // 200 = initialized, unsealed, active
            // 429 = unsealed, standby
            // 472 = data recovery mode
            // 473 = performance standby
            // 501 = not initialized
            // 503 = sealed
            var statusCode = (int)response.StatusCode;

            if (statusCode == 200)
            {
                if (sw.Elapsed.TotalMilliseconds > 2000)
                    return HealthCheckResult.Degraded($"Vault responding slowly: {sw.Elapsed.TotalMilliseconds:F0}ms.", data: data);

                return HealthCheckResult.Healthy($"Vault OK — active ({sw.Elapsed.TotalMilliseconds:F0}ms).", data);
            }

            if (statusCode == 429 || statusCode == 473)
            {
                return HealthCheckResult.Degraded($"Vault is in standby mode (HTTP {statusCode}).", data: data);
            }

            return HealthCheckResult.Unhealthy($"Vault unhealthy (HTTP {statusCode}).", data: data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"Vault connection failed: {ex.Message}", ex);
        }
    }
}
