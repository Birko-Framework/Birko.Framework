using System;
using System.Linq;
using Birko.Data.RavenDB.Stores;
using Birko.Data.Sync.Models;
using Birko.Data.Sync.RavenDB.Models;
using Birko.Data.Sync.Stores;

namespace Birko.Data.Sync.RavenDB.Stores;

/// <summary>
/// RavenDB knowledge store for the synchronous sync engine.
/// </summary>
/// <remarks>
/// TASK-630: implements the sync engine's knowledge-store interface, like every other backend. It used to expose its own
/// tenant API (the <c>ISyncKnowledgeStore</c> shape, which nothing implemented and only <c>TenantSyncProvider</c> called)
/// with its own identity and conversion paths, and those were the defects (SH-M060, SH-M062, SH-M063, SH-M064, SH-M436).
/// The engine upserts by the row's Guid; tenant isolation comes from the tenant provider's tenant-qualified scope.
/// </remarks>
public class RavenSyncKnowledgeStore : RavenDBStore<RavenSyncKnowledgeItem>, ISyncKnowledgeItemStore<RavenSyncKnowledgeItem>
{
    public RavenSyncKnowledgeStore(string connectionString, string? databaseName = null)
        : base(connectionString, databaseName)
    {
    }

    public RavenSyncKnowledgeStore(Raven.Client.Documents.IDocumentStore documentStore)
        : base(documentStore)
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

    public RavenSyncKnowledgeItem CreateKnowledgeItem(Guid guid, string? localItemHash, string? remoteItemHash, SyncOptions options)
    {
        return new RavenSyncKnowledgeItem
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
