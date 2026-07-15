using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Health.Azure;

/// <summary>
/// CR-L264: shared timing/threshold/result boilerplate for the Azure health checks. Wraps a lightweight
/// connectivity probe with a stopwatch, records latency, and maps the outcome to Healthy / Degraded (slow) /
/// Unhealthy (failure) — keeping the cancellation-rethrow behavior (CR-M191) in one place so each new Azure
/// check (Blob, Key Vault, and future ones like Service Bus) doesn't copy it.
/// </summary>
internal static class AzureHealthCheckHelper
{
    /// <summary>The default latency above which a responsive-but-slow probe is reported Degraded.</summary>
    public static readonly TimeSpan DefaultSlowThreshold = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Runs <paramref name="probe"/>, timing it. Returns Degraded when the probe takes longer than
    /// <paramref name="slowThreshold"/> (default 2s), Healthy otherwise; a probe failure maps to Unhealthy.
    /// <see cref="OperationCanceledException"/> is rethrown so the HealthCheckRunner's timeout handling
    /// (which honors the registration's TimeoutStatus) applies instead of being masked (CR-M191).
    /// </summary>
    /// <param name="label">Human-readable service label used in result messages (e.g. "Azure Blob Storage").</param>
    /// <param name="probe">The connectivity probe to time.</param>
    /// <param name="ct">Cancellation token forwarded to the probe.</param>
    /// <param name="slowThreshold">Latency above which the result is Degraded. Defaults to <see cref="DefaultSlowThreshold"/>.</param>
    public static async Task<HealthCheckResult> MeasureAsync(
        string label,
        Func<CancellationToken, Task> probe,
        CancellationToken ct,
        TimeSpan? slowThreshold = null)
    {
        var threshold = slowThreshold ?? DefaultSlowThreshold;
        try
        {
            var sw = Stopwatch.StartNew();
            await probe(ct).ConfigureAwait(false);
            sw.Stop();

            var data = new Dictionary<string, object>
            {
                ["latencyMs"] = Math.Round(sw.Elapsed.TotalMilliseconds, 2)
            };

            if (sw.Elapsed > threshold)
                return HealthCheckResult.Degraded($"{label} responding slowly: {sw.Elapsed.TotalMilliseconds:F0}ms.", data: data);

            return HealthCheckResult.Healthy($"{label} OK ({sw.Elapsed.TotalMilliseconds:F0}ms).", data);
        }
        catch (OperationCanceledException)
        {
            // CR-M191: let cancellation/timeout bubble so HealthCheckRunner's timeout handling applies
            // (honoring the registration's TimeoutStatus, which may be Degraded) instead of masking it
            // as a generic Unhealthy.
            throw;
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"{label} failed: {ex.Message}", ex);
        }
    }
}
