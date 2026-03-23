using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Health.Data;

/// <summary>
/// Health check for Azure Cosmos DB. Calls the account endpoint to verify connectivity.
/// </summary>
public sealed class CosmosDbHealthCheck : IHealthCheck
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    /// <summary>
    /// Creates a Cosmos DB health check.
    /// </summary>
    /// <param name="baseUrl">Cosmos DB account endpoint (e.g., "https://myaccount.documents.azure.com:443").</param>
    /// <param name="httpClient">Optional HttpClient instance.</param>
    public CosmosDbHealthCheck(string baseUrl, HttpClient? httpClient = null)
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
            var sw = Stopwatch.StartNew();
            var response = await _httpClient.GetAsync(_baseUrl, ct).ConfigureAwait(false);
            sw.Stop();

            var data = new Dictionary<string, object>
            {
                ["url"] = _baseUrl,
                ["statusCode"] = (int)response.StatusCode,
                ["latencyMs"] = sw.ElapsedMilliseconds
            };

            if (response.IsSuccessStatusCode || (int)response.StatusCode == 401)
            {
                // 401 is expected without auth headers — endpoint is reachable
                if (sw.ElapsedMilliseconds > 2000)
                {
                    return HealthCheckResult.Degraded($"Cosmos DB reachable but slow ({sw.ElapsedMilliseconds}ms).", data: data);
                }
                return HealthCheckResult.Healthy($"Cosmos DB endpoint OK ({_baseUrl}).", data: data);
            }

            return HealthCheckResult.Unhealthy($"Cosmos DB returned {(int)response.StatusCode}.", data: data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"Cosmos DB connection failed: {ex.Message}", ex);
        }
    }
}
