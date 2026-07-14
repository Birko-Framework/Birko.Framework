using Birko.Data.Sync.Models;
using Birko.Data.Sync.CosmosDB.Models;
using Birko.Data.CosmosDB.Stores;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Sync.CosmosDB.Stores;

/// <summary>
/// Async Cosmos DB implementation of sync knowledge store.
/// </summary>
public class AsyncCosmosSyncKnowledgeStore : AsyncCosmosDBStore<CosmosSyncKnowledgeItem>
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

    /// <summary>
    /// Gets sync knowledge for a specific scope.
    /// </summary>
    public async Task<Dictionary<Guid, ISyncKnowledgeItem>> GetKnowledgeAsync(
        string scope,
        Guid? tenantId,
        CancellationToken ct = default)
    {
        if (Container == null) return new Dictionary<Guid, ISyncKnowledgeItem>();

        // Scope by tenant as well (CR-H100): previously tenantId was ignored, so a query returned —
        // and Delete/SetLastSyncTime affected — every tenant's items in the scope.
        var queryable = Container.GetItemLinqQueryable<CosmosSyncKnowledgeItem>()
            .Where(x => x.Scope == scope && x.TenantId == tenantId);

        var results = new List<CosmosSyncKnowledgeItem>();
        using var iterator = queryable.ToFeedIterator();
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(ct);
            results.AddRange(response);
        }

        return results.ToDictionary(x => x.EntityGuid, x => (ISyncKnowledgeItem)x);
    }

    /// <summary>
    /// Gets a specific sync knowledge item.
    /// </summary>
    public async Task<ISyncKnowledgeItem?> GetKnowledgeItemAsync(
        Guid entityGuid,
        string scope,
        Guid? tenantId,
        CancellationToken ct = default)
    {
        if (Container == null) return null;

        // CR-L210: query directly on Scope + TenantId + EntityGuid and take the first result, instead of
        // materializing every document in the scope into a Dictionary just to pull one item out of it.
        var queryable = Container.GetItemLinqQueryable<CosmosSyncKnowledgeItem>()
            .Where(x => x.Scope == scope && x.TenantId == tenantId && x.EntityGuid == entityGuid)
            .Take(1);

        using var iterator = queryable.ToFeedIterator();
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(ct);
            var first = response.FirstOrDefault();
            if (first != null) return first;
        }

        return null;
    }

    /// <summary>
    /// Updates or creates sync knowledge items.
    /// </summary>
    public async Task UpdateKnowledgeAsync(
        IEnumerable<ISyncKnowledgeItem> items,
        Guid? tenantId = null,
        CancellationToken ct = default)
    {
        if (Container == null) return;

        var tasks = new List<Task>();
        foreach (var item in items)
        {
            var cosmosItem = CosmosSyncKnowledgeItem.FromInterface(item, tenantId);
            tasks.Add(Container.UpsertItemAsync(cosmosItem, new PartitionKey(cosmosItem.Guid!.Value.ToString()), cancellationToken: ct));
        }

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Updates or creates a single sync knowledge item.
    /// </summary>
    public async Task UpdateKnowledgeItemAsync(
        ISyncKnowledgeItem item,
        Guid? tenantId = null,
        CancellationToken ct = default)
    {
        await UpdateKnowledgeAsync(new[] { item }, tenantId, ct);
    }

    /// <summary>
    /// Deletes sync knowledge for a specific scope.
    /// </summary>
    public async Task DeleteKnowledgeAsync(
        string scope,
        Guid? tenantId,
        CancellationToken ct = default)
    {
        if (Container == null) return;

        var knowledge = await GetKnowledgeAsync(scope, tenantId, ct);
        var tasks = new List<Task>();

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

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Gets the last sync time for a scope.
    /// </summary>
    public async Task<DateTime?> GetLastSyncTimeAsync(
        string scope,
        Guid? tenantId,
        CancellationToken ct = default)
    {
        var knowledge = await GetKnowledgeAsync(scope, tenantId, ct);
        return knowledge.Values.Any() ? knowledge.Values.Max(x => (DateTime?)x.LastSyncedAt) : null;
    }

    /// <summary>
    /// Sets the last sync time for all items in a scope.
    /// </summary>
    public async Task SetLastSyncTimeAsync(
        string scope,
        Guid? tenantId,
        DateTime syncTime,
        CancellationToken ct = default)
    {
        if (Container == null) return;

        var knowledge = await GetKnowledgeAsync(scope, tenantId, ct);
        var tasks = new List<Task>();

        foreach (var item in knowledge.Values.Cast<CosmosSyncKnowledgeItem>())
        {
            item.LastSyncedAt = syncTime;
            tasks.Add(Container.ReplaceItemAsync(item, item.Guid!.Value.ToString(),
                new PartitionKey(item.Guid.Value.ToString()), cancellationToken: ct));
        }

        await Task.WhenAll(tasks);
    }
}
