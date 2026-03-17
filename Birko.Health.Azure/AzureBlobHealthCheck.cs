using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Birko.Storage;
using Birko.Storage.AzureBlob;

namespace Birko.Health.Azure;

/// <summary>
/// Health check for Azure Blob Storage connectivity.
/// Verifies the container is accessible by listing blobs (maxResults=1).
/// </summary>
public sealed class AzureBlobHealthCheck : IHealthCheck
{
    private readonly Func<AzureBlobStorage> _storageFactory;

    /// <summary>
    /// Creates a health check using a factory function for the storage instance.
    /// </summary>
    public AzureBlobHealthCheck(Func<AzureBlobStorage> storageFactory)
    {
        _storageFactory = storageFactory ?? throw new ArgumentNullException(nameof(storageFactory));
    }

    /// <summary>
    /// Creates a health check using an existing storage instance.
    /// </summary>
    public AzureBlobHealthCheck(AzureBlobStorage storage)
    {
        if (storage == null) throw new ArgumentNullException(nameof(storage));
        _storageFactory = () => storage;
    }

    public async Task<HealthCheckResult> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            var sw = Stopwatch.StartNew();
            var storage = _storageFactory();

            // List with maxResults=1 is a lightweight connectivity check
            await storage.ListAsync(prefix: null, maxResults: 1, ct: ct).ConfigureAwait(false);

            sw.Stop();
            var data = new Dictionary<string, object>
            {
                ["latencyMs"] = Math.Round(sw.Elapsed.TotalMilliseconds, 2)
            };

            if (sw.Elapsed.TotalMilliseconds > 2000)
                return HealthCheckResult.Degraded($"Azure Blob Storage responding slowly: {sw.Elapsed.TotalMilliseconds:F0}ms.", data: data);

            return HealthCheckResult.Healthy($"Azure Blob Storage OK ({sw.Elapsed.TotalMilliseconds:F0}ms).", data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"Azure Blob Storage failed: {ex.Message}", ex);
        }
    }
}
