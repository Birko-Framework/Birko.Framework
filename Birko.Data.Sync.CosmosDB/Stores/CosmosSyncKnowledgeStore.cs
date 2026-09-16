using Birko.Data.Sync.Models;
using Birko.Data.Sync.CosmosDB.Models;
using Birko.Data.CosmosDB.Stores;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Birko.Data.Sync.CosmosDB.Stores;

/// <summary>
/// Sync Cosmos DB implementation of sync knowledge store.
/// </summary>
public class CosmosSyncKnowledgeStore : CosmosDBStore<CosmosSyncKnowledgeItem>
{
    /// <summary>
    /// Creates a new Cosmos DB sync knowledge store.
    /// </summary>
    public CosmosSyncKnowledgeStore(string connectionString, string databaseName, string? containerName = null)
        : base(connectionString, databaseName, containerName ?? CosmosSyncKnowledgeItem.ContainerName)
    {
    }

    /// <summary>
    /// Creates a new Cosmos DB sync knowledge store with an existing container.
    /// </summary>
    public CosmosSyncKnowledgeStore(Container container)
        : base(container)
    {
    }

    /// <summary>
    /// Gets sync knowledge for a specific scope.
    /// </summary>
    public Dictionary<Guid, ISyncKnowledgeItem> GetKnowledge(
        string scope,
        Guid? tenantId,
        System.Threading.CancellationToken ct = default)
    {
        if (Container == null) return new Dictionary<Guid, ISyncKnowledgeItem>();

        // Scope by tenant as well (CR-H100): previously tenantId was ignored, so a query returned —
        // and Delete/SetLastSyncTime affected — every tenant's items in the scope.
        // SH-H013: the predicate now comes from one producer, which drops the tenant term entirely when no
        // tenant was given. Inline, it was an unconditional `x.TenantId == tenantId` that Cosmos renders as
        // `root["TenantId"] = null` — Undefined in the Cosmos SQL dialect, so it matched nothing at all.
        var queryable = CosmosSyncKnowledgeQuery.ApplyScope(
            Container.GetItemLinqQueryable<CosmosSyncKnowledgeItem>(allowSynchronousQueryExecution: true), scope, tenantId);

        var items = queryable.ToList();
        return items.ToDictionary(x => x.EntityGuid, x => (ISyncKnowledgeItem)x);
    }

    /// <summary>
    /// Gets a specific sync knowledge item.
    /// </summary>
    public ISyncKnowledgeItem? GetKnowledgeItem(
        Guid entityGuid,
        string scope,
        Guid? tenantId,
        System.Threading.CancellationToken ct = default)
    {
        if (Container == null) return null;

        // CR-L210: query directly on Scope + TenantId + EntityGuid and take the first result, instead of
        // materializing every document in the scope into a Dictionary just to pull one item out of it.
        // SH-H013: scope/tenant come from the shared producer — see CosmosSyncKnowledgeQuery.
        var queryable = CosmosSyncKnowledgeQuery.ApplyScope(
                Container.GetItemLinqQueryable<CosmosSyncKnowledgeItem>(allowSynchronousQueryExecution: true), scope, tenantId)
            .Where(x => x.EntityGuid == entityGuid)
            .Take(1);

        return queryable.ToList().FirstOrDefault();
    }

    /// <summary>
    /// Updates or creates sync knowledge items.
    /// </summary>
    public void UpdateKnowledge(
        IEnumerable<ISyncKnowledgeItem> items,
        Guid? tenantId = null,
        System.Threading.CancellationToken ct = default)
    {
        if (Container == null) return;

        var tasks = new List<System.Threading.Tasks.Task>();
        foreach (var item in items)
        {
            var cosmosItem = CosmosSyncKnowledgeItem.FromInterface(item, tenantId);
            tasks.Add(Container.UpsertItemAsync(cosmosItem, new PartitionKey(cosmosItem.Guid!.Value.ToString()), cancellationToken: ct));
        }

        System.Threading.Tasks.Task.WhenAll(tasks).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Updates or creates a single sync knowledge item.
    /// </summary>
    public void UpdateKnowledgeItem(
        ISyncKnowledgeItem item,
        Guid? tenantId = null,
        System.Threading.CancellationToken ct = default)
    {
        UpdateKnowledge(new[] { item }, tenantId, ct);
    }

    /// <summary>
    /// Deletes sync knowledge for a specific scope.
    /// </summary>
    public void DeleteKnowledge(
        string scope,
        Guid? tenantId,
        System.Threading.CancellationToken ct = default)
    {
        if (Container == null) return;

        var knowledge = GetKnowledge(scope, tenantId, ct);
        var tasks = new List<System.Threading.Tasks.Task>();

        foreach (var item in knowledge.Values)
        {
            if (item.Guid.HasValue)
            {
                tasks.Add(Container.DeleteItemAsync<CosmosSyncKnowledgeItem>(
                    item.Guid.Value.ToString(),
                    new PartitionKey(item.Guid.Value.ToString()),
                    cancellationToken: ct));
            }
        }

        System.Threading.Tasks.Task.WhenAll(tasks).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Gets the last sync time for a scope.
    /// </summary>
    public DateTime? GetLastSyncTime(
        string scope,
        Guid? tenantId,
        System.Threading.CancellationToken ct = default)
    {
        var knowledge = GetKnowledge(scope, tenantId, ct);
        return knowledge.Values.Any() ? knowledge.Values.Max(x => (DateTime?)x.LastSyncedAt) : null;
    }

    /// <summary>
    /// Sets the last sync time for all items in a scope.
    /// </summary>
    public void SetLastSyncTime(
        string scope,
        Guid? tenantId,
        DateTime syncTime,
        System.Threading.CancellationToken ct = default)
    {
        if (Container == null) return;

        var knowledge = GetKnowledge(scope, tenantId, ct);
        var tasks = new List<System.Threading.Tasks.Task>();

        foreach (var item in knowledge.Values.Cast<CosmosSyncKnowledgeItem>())
        {
            item.LastSyncedAt = syncTime;
            tasks.Add(Container.ReplaceItemAsync(item, item.Guid!.Value.ToString(),
                new PartitionKey(item.Guid.Value.ToString()), cancellationToken: ct));
        }

        System.Threading.Tasks.Task.WhenAll(tasks).GetAwaiter().GetResult();
    }
}
