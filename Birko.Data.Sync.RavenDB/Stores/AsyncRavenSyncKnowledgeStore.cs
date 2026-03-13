using Birko.Data.Sync.Models;
using Birko.Data.Sync.RavenDB.Models;
using Birko.Data.Sync.Stores;
using Birko.Data.Stores;
using Raven.Client.Documents;
using Raven.Client.Documents.Linq;
using Raven.Client.Documents.Session;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Sync.RavenDB.Stores;

/// <summary>
/// Async RavenDB implementation of ISyncKnowledgeStore for sync knowledge
/// Non-generic async store that uses RavenSyncKnowledgeItem
/// </summary>
public class AsyncRavenSyncKnowledgeStore : AsyncRavenDBStore<RavenSyncKnowledgeItem>
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

    /// <summary>
    /// Get sync knowledge for a specific scope and optional tenant
    /// </summary>
    public async Task<Dictionary<Guid, ISyncKnowledgeItem>> GetKnowledgeAsync(
        string scope,
        Guid? tenantId,
        CancellationToken cancellationToken = default)
    {
        using var session = DocumentStore!.OpenAsyncSession();

        var query = session.Query<RavenSyncKnowledgeItem>()
            .Where(x => x.Scope == scope);

        // Apply tenant filter if needed
        if (tenantId.HasValue)
        {
            query = query.Where(x => x.Guid!.Value.ToString().StartsWith(tenantId.Value.ToString()));
        }

        var items = await query.ToListAsync(cancellationToken);
        return items.ToDictionary(x => x.EntityGuid, x => (ISyncKnowledgeItem)x);
    }

    /// <summary>
    /// Get a specific sync knowledge item
    /// </summary>
    public async Task<ISyncKnowledgeItem?> GetKnowledgeItemAsync(
        Guid entityGuid,
        string scope,
        Guid? tenantId,
        CancellationToken cancellationToken = default)
    {
        var knowledge = await GetKnowledgeAsync(scope, tenantId, cancellationToken);
        return knowledge.TryGetValue(entityGuid, out var item) ? item : null;
    }

    /// <summary>
    /// Update or create sync knowledge items
    /// </summary>
    public async Task UpdateKnowledgeAsync(
        IEnumerable<ISyncKnowledgeItem> items,
        CancellationToken cancellationToken = default)
    {
        using var session = DocumentStore!.OpenAsyncSession();

        foreach (var item in items)
        {
            var ravenItem = ConvertToRavenItem(item);
            await session.StoreAsync(ravenItem, cancellationToken);
        }

        await session.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Update or create a single sync knowledge item
    /// </summary>
    public async Task UpdateKnowledgeItemAsync(
        ISyncKnowledgeItem item,
        CancellationToken cancellationToken = default)
    {
        await UpdateKnowledgeAsync(new[] { item }, cancellationToken);
    }

    /// <summary>
    /// Delete sync knowledge for a specific scope and optional tenant
    /// </summary>
    public async Task DeleteKnowledgeAsync(
        string scope,
        Guid? tenantId,
        CancellationToken cancellationToken = default)
    {
        using var session = DocumentStore!.OpenAsyncSession();

        var query = session.Query<RavenSyncKnowledgeItem>()
            .Where(x => x.Scope == scope);

        if (tenantId.HasValue)
        {
            query = query.Where(x => x.Guid!.Value.ToString().StartsWith(tenantId.Value.ToString()));
        }

        var items = await query.ToListAsync(cancellationToken);
        foreach (var item in items)
        {
            session.Delete(item.Guid);
        }

        await session.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Get the last sync time for a scope and optional tenant
    /// </summary>
    public async Task<DateTime?> GetLastSyncTimeAsync(
        string scope,
        Guid? tenantId,
        CancellationToken cancellationToken = default)
    {
        var knowledge = await GetKnowledgeAsync(scope, tenantId, cancellationToken);
        return knowledge.Values.Any() ? knowledge.Values.Max(x => (DateTime?)x.LastSyncedAt) : null;
    }

    /// <summary>
    /// Set the last sync time for a scope and optional tenant
    /// </summary>
    public async Task SetLastSyncTimeAsync(
        string scope,
        Guid? tenantId,
        DateTime syncTime,
        CancellationToken cancellationToken = default)
    {
        using var session = DocumentStore!.OpenAsyncSession();

        var query = session.Query<RavenSyncKnowledgeItem>()
            .Where(x => x.Scope == scope);

        if (tenantId.HasValue)
        {
            query = query.Where(x => x.Guid!.Value.ToString().StartsWith(tenantId.Value.ToString()));
        }

        var items = await query.ToListAsync(cancellationToken);
        foreach (var item in items)
        {
            item.LastSyncedAt = syncTime;
        }

        await session.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Convert ISyncKnowledgeItem to RavenSyncKnowledgeItem
    /// </summary>
    private RavenSyncKnowledgeItem ConvertToRavenItem(ISyncKnowledgeItem item)
    {
        if (item is RavenSyncKnowledgeItem ravenItem)
        {
            return ravenItem;
        }

        return new RavenSyncKnowledgeItem
        {
            Guid = item.Guid ?? Guid.NewGuid(),
            EntityGuid = item.EntityGuid,
            Scope = item.Scope,
            LastSyncedAt = item.LastSyncedAt,
            LocalVersion = item.LocalVersion,
            RemoteVersion = item.RemoteVersion,
            IsLocalDeleted = item.IsLocalDeleted,
            IsRemoteDeleted = item.IsRemoteDeleted,
            Metadata = item.Metadata
        };
    }

    /// <summary>
    /// Convert ISyncKnowledgeItem to RavenSyncKnowledgeItem (async version)
    /// </summary>
    private async Task<RavenSyncKnowledgeItem> ConvertToRavenItemAsync(
        ISyncKnowledgeItem item,
        CancellationToken cancellationToken)
    {
        return await System.Threading.Tasks.Task.FromResult(ConvertToRavenItem(item));
    }
}
