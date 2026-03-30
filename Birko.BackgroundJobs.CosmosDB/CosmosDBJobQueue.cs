using Birko.BackgroundJobs.CosmosDB.Models;
using Birko.Data.CosmosDB.Stores;
using Birko.Data.Stores;
using Birko.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.BackgroundJobs.CosmosDB;

/// <summary>
/// Cosmos DB implementation of IJobQueue using AsyncCosmosDBStore.
/// </summary>
public class CosmosDBJobQueue : IJobQueue
{
    private readonly AsyncCosmosDBStore<CosmosJobDescriptorModel> _store;
    private bool _initialized;

    /// <summary>
    /// Gets the underlying store for transaction context access.
    /// </summary>
    public AsyncCosmosDBStore<CosmosJobDescriptorModel> Store => _store;

    /// <summary>
    /// Creates a new Cosmos DB job queue with settings.
    /// </summary>
    public CosmosDBJobQueue(RemoteSettings settings)
    {
        _store = new AsyncCosmosDBStore<CosmosJobDescriptorModel>();
        _store.SetSettings(settings);
    }

    /// <summary>
    /// Creates a new Cosmos DB job queue with an existing store.
    /// </summary>
    public CosmosDBJobQueue(AsyncCosmosDBStore<CosmosJobDescriptorModel> store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _initialized = true;
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (!_initialized)
        {
            await _store.InitAsync(ct).ConfigureAwait(false);
            _initialized = true;
        }
    }

    /// <inheritdoc />
    public async Task<Guid> EnqueueAsync(JobDescriptor descriptor, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct).ConfigureAwait(false);
        var model = CosmosJobDescriptorModel.FromDescriptor(descriptor);
        var id = await _store.CreateAsync(model, ct: ct).ConfigureAwait(false);
        return id;
    }

    /// <inheritdoc />
    public async Task<JobDescriptor?> DequeueAsync(string? queueName = null, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct).ConfigureAwait(false);

        var now = DateTime.UtcNow;
        var pendingStatus = (int)JobStatus.Pending;
        var scheduledStatus = (int)JobStatus.Scheduled;

        var results = await _store.ReadAsync(
            filter: j => (j.Status == pendingStatus || (j.Status == scheduledStatus && j.ScheduledAt <= now))
                && (queueName == null || j.QueueName == queueName),
            orderBy: OrderBy<CosmosJobDescriptorModel>.ByName(nameof(CosmosJobDescriptorModel.Priority), descending: true),
            limit: 1,
            ct: ct
        ).ConfigureAwait(false);

        var model = results.FirstOrDefault();
        if (model == null) return null;

        model.Status = (int)JobStatus.Processing;
        model.LastAttemptAt = DateTime.UtcNow;
        model.AttemptCount++;
        await _store.UpdateAsync(model, ct: ct).ConfigureAwait(false);

        return model.ToDescriptor();
    }

    /// <inheritdoc />
    public async Task CompleteAsync(Guid jobId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct).ConfigureAwait(false);

        var model = await _store.ReadAsync(jobId, ct).ConfigureAwait(false);
        if (model == null) return;

        model.Status = (int)JobStatus.Completed;
        model.CompletedAt = DateTime.UtcNow;
        await _store.UpdateAsync(model, ct: ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task FailAsync(Guid jobId, string? error = null, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct).ConfigureAwait(false);

        var model = await _store.ReadAsync(jobId, ct).ConfigureAwait(false);
        if (model == null) return;

        model.LastError = error;
        if (model.AttemptCount >= model.MaxRetries)
        {
            model.Status = (int)JobStatus.Failed;
            model.CompletedAt = DateTime.UtcNow;
        }
        else
        {
            model.Status = (int)JobStatus.Pending;
        }

        await _store.UpdateAsync(model, ct: ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> CancelAsync(Guid jobId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct).ConfigureAwait(false);

        var model = await _store.ReadAsync(jobId, ct).ConfigureAwait(false);
        if (model == null) return false;

        model.Status = (int)JobStatus.Cancelled;
        model.CompletedAt = DateTime.UtcNow;
        await _store.UpdateAsync(model, ct: ct).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc />
    public async Task<JobDescriptor?> GetAsync(Guid jobId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct).ConfigureAwait(false);

        var model = await _store.ReadAsync(jobId, ct).ConfigureAwait(false);
        return model?.ToDescriptor();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<JobDescriptor>> GetByStatusAsync(JobStatus status, int limit = 100, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct).ConfigureAwait(false);

        var statusInt = (int)status;
        var results = await _store.ReadAsync(
            filter: j => j.Status == statusInt,
            orderBy: OrderBy<CosmosJobDescriptorModel>.ByName(nameof(CosmosJobDescriptorModel.EnqueuedAt), descending: true),
            limit: limit,
            ct: ct
        ).ConfigureAwait(false);

        return results.Select(m => m.ToDescriptor()).ToList();
    }

    /// <inheritdoc />
    public async Task<int> PurgeAsync(TimeSpan olderThan, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct).ConfigureAwait(false);

        var cutoff = DateTime.UtcNow - olderThan;
        var completedStatus = (int)JobStatus.Completed;
        var failedStatus = (int)JobStatus.Failed;
        var cancelledStatus = (int)JobStatus.Cancelled;

        var results = await _store.ReadAsync(
            filter: j => (j.Status == completedStatus || j.Status == failedStatus || j.Status == cancelledStatus)
                && j.CompletedAt != null && j.CompletedAt < cutoff,
            ct: ct
        ).ConfigureAwait(false);

        var count = results.Count();
        if (count > 0)
        {
            await _store.DeleteAsync(results, ct).ConfigureAwait(false);
        }
        return count;
    }
}
