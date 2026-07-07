using Birko.Data.Sync.Models;
using Birko.Data.Sync.RavenDB.Models;
using Birko.Data.Sync.Stores;
using Birko.Data.RavenDB.Stores;
using Birko.Data.Stores;
using Birko.Data.Tenant.Models;
using Birko.Configuration;
using System.Linq;
using System.Linq.Expressions;
using Birko.Data.Models;
using Birko.Data.Repositories;

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
    public Dictionary<Guid, ISyncKnowledgeItem> GetKnowledgeAsync(
        string scope,
        Guid? tenantId,
        System.Threading.CancellationToken cancellationToken = default)
    {
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
    public ISyncKnowledgeItem? GetKnowledgeItemAsync(
        Guid entityGuid,
        string scope,
        Guid? tenantId,
        System.Threading.CancellationToken cancellationToken = default)
    {
        var knowledge = GetKnowledgeAsync(scope, tenantId, cancellationToken);
        return knowledge.TryGetValue(entityGuid, out var item) ? item : null;
    }

    /// <summary>
    /// Update or create sync knowledge items
    /// </summary>
    public void UpdateKnowledgeAsync(
        IEnumerable<ISyncKnowledgeItem> items,
        System.Threading.CancellationToken cancellationToken = default)
    {
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
    public void UpdateKnowledgeItemAsync(
        ISyncKnowledgeItem item,
        System.Threading.CancellationToken cancellationToken = default)
    {
        UpdateKnowledgeAsync(new[] { item }, cancellationToken);
    }

    /// <summary>
    /// Delete sync knowledge for a specific scope and optional tenant
    /// </summary>
    public void DeleteKnowledgeAsync(
        string scope,
        Guid? tenantId,
        System.Threading.CancellationToken cancellationToken = default)
    {
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
    public System.DateTime? GetLastSyncTimeAsync(
        string scope,
        Guid? tenantId,
        System.Threading.CancellationToken cancellationToken = default)
    {
        var knowledge = GetKnowledgeAsync(scope, tenantId, cancellationToken);
        return knowledge.Values.Any() ? knowledge.Values.Max(x => (System.DateTime?)x.LastSyncedAt) : null;
    }

    /// <summary>
    /// Set the last sync time for a scope and optional tenant
    /// </summary>
    public void SetLastSyncTimeAsync(
        string scope,
        Guid? tenantId,
        System.DateTime syncTime,
        System.Threading.CancellationToken cancellationToken = default)
    {
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
