using Birko.Data.Sync.Models;
using Birko.Data.Sync.RavenDB.Models;
using Birko.Data.RavenDB.Stores;
using System.Linq;

namespace Birko.Data.Sync.RavenDB.Stores;

/// <summary>
/// RavenDB implementation of ISyncKnowledgeStore for sync knowledge
/// Non-generic store that uses RavenSyncKnowledgeItem
/// </summary>
public class RavenSyncKnowledgeStore : RavenDBStore<RavenSyncKnowledgeItem>
{
    /// <summary>
    /// Create a new RavenDB sync knowledge store
    /// </summary>
    public RavenSyncKnowledgeStore(string connectionString, string? databaseName = null)
        : base(connectionString, databaseName)
    {
    }

    /// <summary>
    /// Create a new RavenDB sync knowledge store with existing document store
    /// </summary>
    public RavenSyncKnowledgeStore(Raven.Client.Documents.IDocumentStore documentStore)
        : base(documentStore)
    {
    }

    /// <summary>
    /// Get sync knowledge for a specific scope and optional tenant
    /// </summary>
    public Dictionary<Guid, ISyncKnowledgeItem> GetKnowledge(
        string scope,
        Guid? tenantId,
        System.Threading.CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var session = DocumentStore!.OpenSession();

        var query = session.Query<RavenSyncKnowledgeItem>()
            .Where(x => x.Scope == scope);

        // Scope to the tenant carried on each item (ITenant.TenantGuid), mirroring ModelByTenant.
        if (tenantId.HasValue)
        {
            query = query.Where(x => x.TenantGuid == tenantId.Value);
        }

        var items = query.ToList();
        return items.ToDictionary(x => x.EntityGuid, x => (ISyncKnowledgeItem)x);
    }

    /// <summary>
    /// Get a specific sync knowledge item
    /// </summary>
    public ISyncKnowledgeItem? GetKnowledgeItem(
        Guid entityGuid,
        string scope,
        Guid? tenantId,
        System.Threading.CancellationToken cancellationToken = default)
    {
        var knowledge = GetKnowledge(scope, tenantId, cancellationToken);
        return knowledge.TryGetValue(entityGuid, out var item) ? item : null;
    }

    /// <summary>
    /// Update or create sync knowledge items
    /// </summary>
    public void UpdateKnowledge(
        IEnumerable<ISyncKnowledgeItem> items,
        System.Threading.CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var session = DocumentStore!.OpenSession();

        foreach (var item in items)
        {
            var ravenItem = ConvertToRavenItem(item);
            session.Store(ravenItem);
        }

        session.SaveChanges();
    }

    /// <summary>
    /// Update or create a single sync knowledge item
    /// </summary>
    public void UpdateKnowledgeItem(
        ISyncKnowledgeItem item,
        System.Threading.CancellationToken cancellationToken = default)
    {
        UpdateKnowledge(new[] { item }, cancellationToken);
    }

    /// <summary>
    /// Delete sync knowledge for a specific scope and optional tenant
    /// </summary>
    public void DeleteKnowledge(
        string scope,
        Guid? tenantId,
        System.Threading.CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var session = DocumentStore!.OpenSession();

        var query = session.Query<RavenSyncKnowledgeItem>()
            .Where(x => x.Scope == scope);

        if (tenantId.HasValue)
        {
            query = query.Where(x => x.TenantGuid == tenantId.Value);
        }

        var items = query.ToList();
        foreach (var item in items)
        {
            session.Delete(item);
        }

        session.SaveChanges();
    }

    /// <summary>
    /// Get the last sync time for a scope and optional tenant
    /// </summary>
    public System.DateTime? GetLastSyncTime(
        string scope,
        Guid? tenantId,
        System.Threading.CancellationToken cancellationToken = default)
    {
        var knowledge = GetKnowledge(scope, tenantId, cancellationToken);
        return knowledge.Values.Any() ? knowledge.Values.Max(x => (System.DateTime?)x.LastSyncedAt) : null;
    }

    /// <summary>
    /// Set the last sync time for a scope and optional tenant
    /// </summary>
    public void SetLastSyncTime(
        string scope,
        Guid? tenantId,
        System.DateTime syncTime,
        System.Threading.CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var session = DocumentStore!.OpenSession();

        var query = session.Query<RavenSyncKnowledgeItem>()
            .Where(x => x.Scope == scope);

        if (tenantId.HasValue)
        {
            query = query.Where(x => x.TenantGuid == tenantId.Value);
        }

        var items = query.ToList();
        foreach (var item in items)
        {
            item.LastSyncedAt = syncTime;
        }

        session.SaveChanges();
    }

    /// <summary>
    /// Convert ISyncKnowledgeItem to RavenSyncKnowledgeItem
    /// </summary>
    // Delegates to the async store's shared logic, which derives a deterministic Guid from the
    // natural key so re-syncs upsert instead of creating duplicate documents (CR-H103).
    private static RavenSyncKnowledgeItem ConvertToRavenItem(ISyncKnowledgeItem item)
        => AsyncRavenSyncKnowledgeStore.ConvertToRavenItem(item);
}
