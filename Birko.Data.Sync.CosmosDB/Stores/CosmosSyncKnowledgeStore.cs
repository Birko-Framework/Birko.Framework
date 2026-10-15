using System;
using System.Linq;
using Birko.Data.CosmosDB.Stores;
using Birko.Data.Sync.CosmosDB.Models;
using Birko.Data.Sync.Models;
using Birko.Data.Sync.Stores;
using Microsoft.Azure.Cosmos;

namespace Birko.Data.Sync.CosmosDB.Stores;

/// <summary>
/// Cosmos DB knowledge store for the synchronous sync engine.
/// </summary>
/// <remarks>
/// TASK-630: implements the sync engine's knowledge-store interface, like every other backend. It used to expose its own
/// tenant API (the <c>ISyncKnowledgeStore</c> shape, which nothing implemented and only <c>TenantSyncProvider</c> called)
/// with its own identity and conversion paths, and those were the defects (SH-M060, SH-M062, SH-M063, SH-M064, SH-M436).
/// The engine upserts by the row's Guid; tenant isolation comes from the tenant provider's tenant-qualified scope.
/// </remarks>
public class CosmosSyncKnowledgeStore : CosmosDBStore<CosmosSyncKnowledgeItem>, ISyncKnowledgeItemStore<CosmosSyncKnowledgeItem>
{
    public CosmosSyncKnowledgeStore(string connectionString, string databaseName, string? containerName = null)
        : base(connectionString, databaseName, containerName ?? CosmosSyncKnowledgeItem.ContainerName)
    {
    }

    public CosmosSyncKnowledgeStore(Container container)
        : base(container)
    {
    }

    public DateTime? GetLastSyncTime(string scope)
    {
        var items = Read(x => x.Scope == scope, null, null, null).ToList();
        return items.Count > 0 ? items.Max(x => (DateTime?)x.LastSyncedAt) : null;
    }

    /// <summary>
    /// Stamps every row of the scope; <c>null</c> when the scope has none (nothing persisted, SH-M067).
    /// </summary>
    public DateTime? SetLastSyncTime(string scope, DateTime? lastSyncTime)
    {
        if (lastSyncTime == null) return null;

        var items = Read(x => x.Scope == scope, null, null, null).ToList();
        if (items.Count == 0)
        {
            return null;
        }

        foreach (var item in items)
        {
            item.LastSyncedAt = lastSyncTime.Value;
        }
        Update(items);
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
