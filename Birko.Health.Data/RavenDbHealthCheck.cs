using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Health.Data;

/// <summary>
/// Health check for RavenDB. Calls the server build endpoint.
/// </summary>
public sealed class RavenDbHealthCheck : IHealthCheck
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    /// <summary>
    /// Creates a RavenDB health check.
    /// </summary>
    /// <param name="baseUrl">RavenDB server URL (e.g., "http://localhost:8080").</param>
    /// <param name="httpClient">Optional HttpClient instance.</param>
    public RavenDbHealthCheck(string baseUrl, HttpClient? httpClient = null)
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
            var response = await _httpClient.GetAsync($"{_baseUrl}/build/version", ct).ConfigureAwait(false);

            var data = new Dictionary<string, object>
            {
                ["url"] = _baseUrl,
                ["statusCode"] = (int)response.StatusCode
            };

            if (response.IsSuccessStatusCode)
            {
                return HealthCheckResult.Healthy($"RavenDB server OK ({_baseUrl}).", data);
            }

            return HealthCheckResult.Unhealthy($"RavenDB returned {(int)response.StatusCode}.", data: data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"RavenDB connection failed: {ex.Message}", ex);
        }
    }
}
