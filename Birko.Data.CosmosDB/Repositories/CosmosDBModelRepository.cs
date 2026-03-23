using Birko.Data.Models;
using Birko.Data.Repositories;
using Birko.Data.CosmosDB.Stores;
using Birko.Data.Stores;
using Birko.Configuration;
using Microsoft.Azure.Cosmos;
using System;

namespace Birko.Data.CosmosDB.Repositories;

/// <summary>
/// Cosmos DB repository for direct model access with bulk support.
/// </summary>
/// <typeparam name="T">The type of data model.</typeparam>
public class CosmosDBModelRepository<T> : AbstractBulkRepository<T>
    where T : AbstractModel
{
    /// <summary>
    /// Gets the Cosmos DB store.
    /// </summary>
    public CosmosDBStore<T>? CosmosStore => Store?.GetUnwrappedStore<T, CosmosDBStore<T>>();

    public CosmosDBModelRepository()
        : base(null)
    {
        Store = new CosmosDBStore<T>();
    }

    public CosmosDBModelRepository(string connectionString, string databaseName, string? containerName = null)
        : base(null)
    {
        Store = new CosmosDBStore<T>(connectionString, databaseName, containerName);
    }

    public CosmosDBModelRepository(Container container)
        : base(null)
    {
        Store = new CosmosDBStore<T>(container);
    }

    public CosmosDBModelRepository(IStore<T>? store)
        : base(null)
    {
        if (store != null && !store.IsStoreOfType<T, CosmosDBStore<T>>())
        {
            throw new ArgumentException(
                "Store must be of type CosmosDBStore<T> or a wrapper around it.",
                nameof(store));
        }
        Store = store ?? new CosmosDBStore<T>();
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
}
