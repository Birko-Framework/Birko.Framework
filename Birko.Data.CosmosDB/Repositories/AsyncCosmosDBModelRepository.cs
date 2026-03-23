using Birko.Data.Models;
using Birko.Data.CosmosDB.Stores;
using Birko.Data.Stores;
using Birko.Configuration;
using Microsoft.Azure.Cosmos;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.CosmosDB.Repositories;

/// <summary>
/// Async Cosmos DB repository for direct model access with bulk support.
/// </summary>
/// <typeparam name="T">The type of data model.</typeparam>
public class AsyncCosmosDBModelRepository<T> : Data.Repositories.AbstractAsyncBulkRepository<T>
    where T : AbstractModel
{
    /// <summary>
    /// Gets the Cosmos DB async store.
    /// </summary>
    public AsyncCosmosDBStore<T>? CosmosStore => Store?.GetUnwrappedStore<T, AsyncCosmosDBStore<T>>();

    public AsyncCosmosDBModelRepository()
        : base(null)
    {
        Store = new AsyncCosmosDBStore<T>();
    }

    public AsyncCosmosDBModelRepository(string connectionString, string databaseName, string? containerName = null)
        : base(null)
    {
        Store = new AsyncCosmosDBStore<T>(connectionString, databaseName, containerName);
    }

    public AsyncCosmosDBModelRepository(Container container)
        : base(null)
    {
        Store = new AsyncCosmosDBStore<T>(container);
    }

    public AsyncCosmosDBModelRepository(Data.Stores.IAsyncStore<T>? store)
        : base(null)
    {
        if (store != null && !store.IsStoreOfType<T, AsyncCosmosDBStore<T>>())
        {
            throw new ArgumentException(
                "Store must be of type AsyncCosmosDBStore<T> or a wrapper around it.",
                nameof(store));
        }
        Store = store ?? new AsyncCosmosDBStore<T>();
    }

    public void SetSettings(RemoteSettings settings)
    {
        if (settings != null && CosmosStore != null)
        {
            CosmosStore.SetSettings(settings);
        }
    }

    public bool IsHealthy()
    {
        return CosmosStore?.IsHealthy() ?? false;
    }

    public override async Task DestroyAsync(CancellationToken ct = default)
    {
        await base.DestroyAsync(ct);
        if (CosmosStore != null)
        {
            await CosmosStore.DestroyAsync(ct);
        }
    }
}
