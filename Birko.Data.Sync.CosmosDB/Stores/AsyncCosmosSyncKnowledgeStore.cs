using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.CosmosDB.Stores;
using Birko.Data.Sync.CosmosDB.Models;
using Birko.Data.Sync.Models;
using Birko.Data.Sync.Stores;
using Microsoft.Azure.Cosmos;

namespace Birko.Data.Sync.CosmosDB.Stores;

/// <summary>
/// Async Cosmos DB knowledge store for the sync engine.
/// </summary>
/// <remarks>
/// TASK-630: implements the sync engine's knowledge-store interface, like every other backend. It used to expose its own
/// tenant API (the <c>ISyncKnowledgeStore</c> shape, which nothing implemented and only <c>TenantSyncProvider</c> called)
/// with its own identity and conversion paths, and those were the defects (SH-M060, SH-M062, SH-M063, SH-M064, SH-M436).
/// The engine upserts by the row's Guid; tenant isolation comes from the tenant provider's tenant-qualified scope.
/// </remarks>
public class AsyncCosmosSyncKnowledgeStore : AsyncCosmosDBStore<CosmosSyncKnowledgeItem>, IAsyncSyncKnowledgeItemStore<CosmosSyncKnowledgeItem>
{
    /// <summary>
    /// Creates a new async Cosmos DB sync knowledge store.
    /// </summary>
    public AsyncCosmosSyncKnowledgeStore(string connectionString, string databaseName, string? containerName = null)
        : base(connectionString, databaseName, containerName ?? CosmosSyncKnowledgeItem.ContainerName)
    {
    }

    /// <summary>
    /// Creates a new async Cosmos DB sync knowledge store with an existing container.
    /// </summary>
    public AsyncCosmosSyncKnowledgeStore(Container container)
        : base(container)
    {
    }

    public async Task<DateTime?> GetLastSyncTimeAsync(string scope, CancellationToken cancellationToken)
    {
        var items = (await ReadAsync(x => x.Scope == scope, null, null, null, cancellationToken).ConfigureAwait(false)).ToList();
        return items.Count > 0 ? items.Max(x => (DateTime?)x.LastSyncedAt) : null;
    }

    /// <summary>
    /// Stamps every row of the scope; <c>null</c> when the scope has none (nothing persisted, SH-M067).
    /// </summary>
    public async Task<DateTime?> SetLastSyncTimeAsync(string scope, DateTime? lastSyncTime, CancellationToken cancellationToken)
    {
        if (lastSyncTime == null) return null;

        var items = (await ReadAsync(x => x.Scope == scope, null, null, null, cancellationToken).ConfigureAwait(false)).ToList();
        if (items.Count == 0)
        {
            return null;
        }

        foreach (var item in items)
        {
            item.LastSyncedAt = lastSyncTime.Value;
        }
        await UpdateAsync(items, null, cancellationToken).ConfigureAwait(false);
        return lastSyncTime;
    }

    public CosmosSyncKnowledgeItem CreateKnowledgeItem(Guid guid, string? localItemHash, string? remoteItemHash, SyncOptions options)
    {
        return new CosmosSyncKnowledgeItem
        {
            Guid = Guid.NewGuid(),
            EntityGuid = guid,
            Scope = options.Scope,
            LastSyncedAt = DateTime.UtcNow,
            LocalVersion = localItemHash,
            RemoteVersion = remoteItemHash,
            IsLocalDeleted = string.IsNullOrEmpty(localItemHash),
            IsRemoteDeleted = string.IsNullOrEmpty(remoteItemHash)
        };
    }
}
