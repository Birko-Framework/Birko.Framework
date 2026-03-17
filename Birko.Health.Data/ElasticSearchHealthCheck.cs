using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Health.Data;

/// <summary>
/// Health check for Elasticsearch. Calls the cluster health API.
/// </summary>
public sealed class ElasticSearchHealthCheck : IHealthCheck
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    /// <summary>
    /// Creates an Elasticsearch health check.
    /// </summary>
    /// <param name="baseUrl">Elasticsearch base URL (e.g., "http://localhost:9200").</param>
    /// <param name="httpClient">Optional HttpClient instance. If null, a new one is created.</param>
    public ElasticSearchHealthCheck(string baseUrl, HttpClient? httpClient = null)
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
            var response = await _httpClient.GetAsync($"{_baseUrl}/_cluster/health", ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return HealthCheckResult.Unhealthy($"Elasticsearch returned {(int)response.StatusCode}: {body}");
            }

            var data = new Dictionary<string, object>
            {
                ["url"] = _baseUrl,
                ["statusCode"] = (int)response.StatusCode
            };

            // Parse cluster status from response (green/yellow/red)
            if (body.Contains("\"status\":\"red\""))
            {
                return HealthCheckResult.Unhealthy("Elasticsearch cluster status: red.", data: data);
            }

            if (body.Contains("\"status\":\"yellow\""))
            {
                return HealthCheckResult.Degraded("Elasticsearch cluster status: yellow.", data: data);
            }

            return HealthCheckResult.Healthy("Elasticsearch cluster status: green.", data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"Elasticsearch connection failed: {ex.Message}", ex);
        }
    }
}
