using System;
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

    public Task<HealthCheckResult> CheckAsync(CancellationToken ct = default)
        // CR-L264: timing/threshold/result boilerplate lives in the shared helper.
        // List with maxResults=1 is a lightweight connectivity check.
        => AzureHealthCheckHelper.MeasureAsync(
            "Azure Blob Storage",
            c => _storageFactory().ListAsync(prefix: null, maxResults: 1, ct: c),
            ct);
}
