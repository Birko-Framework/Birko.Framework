using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Health.Data;

/// <summary>
/// Health check for a Server-Sent Events (SSE) endpoint. Tests connectivity by sending
/// an HTTP GET request and verifying the response content type is text/event-stream.
/// </summary>
public sealed class SseHealthCheck : IHealthCheck
{
    private readonly string _url;
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Creates an SSE health check.
    /// </summary>
    /// <param name="url">SSE endpoint URL.</param>
    /// <param name="httpClient">Optional HttpClient instance. If not provided, a new one is created.</param>
    public SseHealthCheck(string url, HttpClient? httpClient = null)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException("URL cannot be null or empty.", nameof(url));
        }

        _url = url.TrimEnd('/');
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<HealthCheckResult> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            var sw = Stopwatch.StartNew();

            using var request = new HttpRequestMessage(HttpMethod.Get, _url);
            request.Headers.Accept.ParseAdd("text/event-stream");

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

            sw.Stop();

            var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;

            var data = new Dictionary<string, object>
            {
                ["url"] = _url,
                ["latencyMs"] = Math.Round(sw.Elapsed.TotalMilliseconds, 2),
                ["statusCode"] = (int)response.StatusCode,
                ["contentType"] = contentType
            };

            if (!response.IsSuccessStatusCode)
            {
                return HealthCheckResult.Unhealthy($"SSE endpoint returned {(int)response.StatusCode}.", data: data);
            }

            if (!contentType.Equals("text/event-stream", StringComparison.OrdinalIgnoreCase))
            {
                return HealthCheckResult.Degraded($"SSE endpoint returned unexpected content type: {contentType}.", data: data);
            }

            if (sw.Elapsed.TotalMilliseconds > 2000)
            {
                return HealthCheckResult.Degraded($"SSE endpoint responding slowly: {sw.Elapsed.TotalMilliseconds:F0}ms.", data: data);
            }

            return HealthCheckResult.Healthy($"SSE ({_url}) OK ({sw.Elapsed.TotalMilliseconds:F0}ms).", data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"SSE ({_url}) connection failed: {ex.Message}", ex);
        }
    }
}
