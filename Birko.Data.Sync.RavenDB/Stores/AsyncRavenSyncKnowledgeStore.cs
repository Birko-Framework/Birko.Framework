using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.RavenDB.Stores;
using Birko.Data.Sync.Models;
using Birko.Data.Sync.RavenDB.Models;
using Birko.Data.Sync.Stores;

namespace Birko.Data.Sync.RavenDB.Stores;

/// <summary>
/// Async RavenDB knowledge store for the sync engine.
/// </summary>
/// <remarks>
/// TASK-630: implements the sync engine's knowledge-store interface, like every other backend. It used to expose its own
/// tenant API (the <c>ISyncKnowledgeStore</c> shape, which nothing implemented and only <c>TenantSyncProvider</c> called)
/// with its own identity and conversion paths, and those were the defects (SH-M060, SH-M062, SH-M063, SH-M064, SH-M436).
/// The engine upserts by the row's Guid; tenant isolation comes from the tenant provider's tenant-qualified scope.
/// </remarks>
public class AsyncRavenSyncKnowledgeStore : AsyncRavenDBStore<RavenSyncKnowledgeItem>, IAsyncSyncKnowledgeItemStore<RavenSyncKnowledgeItem>
{
    /// <summary>
    /// Create a new async RavenDB sync knowledge store
    /// </summary>
    public AsyncRavenSyncKnowledgeStore(string connectionString, string? databaseName = null)
        : base(connectionString, databaseName)
    {
    }

    /// <summary>
    /// Create a new async RavenDB sync knowledge store with existing document store
    /// </summary>
    public AsyncRavenSyncKnowledgeStore(Raven.Client.Documents.IDocumentStore documentStore)
        : base(documentStore)
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
