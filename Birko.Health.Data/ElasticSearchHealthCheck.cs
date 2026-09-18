using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Health.Data;

/// <summary>
/// Health check for Elasticsearch. Calls the cluster health API.
/// </summary>
public sealed class ElasticSearchHealthCheck : IHealthCheck, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;
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
        _ownsClient = httpClient is null;
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

            // CR-L267: read the top-level "status" field from the parsed JSON instead of substring-matching
            // `"status":"red"` — the old check depended on exact whitespace/field-ordering and could be
            // fooled by a nested object also carrying a status field.
            var clusterStatus = ReadClusterStatus(body);
            if (clusterStatus != null)
            {
                data["clusterStatus"] = clusterStatus;
            }

            if (string.Equals(clusterStatus, "red", StringComparison.OrdinalIgnoreCase))
            {
                return HealthCheckResult.Unhealthy("Elasticsearch cluster status: red.", data: data);
            }

            if (string.Equals(clusterStatus, "yellow", StringComparison.OrdinalIgnoreCase))
            {
                return HealthCheckResult.Degraded("Elasticsearch cluster status: yellow.", data: data);
            }

            return HealthCheckResult.Healthy($"Elasticsearch cluster status: {clusterStatus ?? "green"}.", data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"Elasticsearch connection failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Reads the top-level <c>status</c> string from the cluster-health JSON body, or null if the body
    /// isn't a JSON object with a string <c>status</c> (CR-L267). A 200 with a non-JSON body reads as
    /// "unknown" (null) and is treated as Healthy, matching the previous fall-through behavior.
    /// </summary>
    private static string? ReadClusterStatus(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("status", out var statusProp) &&
                statusProp.ValueKind == JsonValueKind.String)
            {
                return statusProp.GetString();
            }
        }
        catch (JsonException)
        {
            // Non-JSON body — treat cluster status as unknown (the HTTP success check already passed).
        }

        return null;
    }

    /// <summary>
    /// Disposes the internally-created HttpClient (CR-M192). A caller-supplied client is
    /// left alone — the caller owns its lifetime.
    /// </summary>
    public void Dispose()
    {
        if (_ownsClient)
        {
            _httpClient.Dispose();
        }
    }
}
