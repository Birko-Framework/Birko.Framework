using Birko.Data.Models;
using Birko.Data.Repositories;
using Birko.Data.CosmosDB.Stores;
using Birko.Data.Stores;
using Birko.Configuration;
using Microsoft.Azure.Cosmos;
using System;

namespace Birko.Data.CosmosDB.Repositories;

/// <summary>
/// Cosmos DB repository with bulk operations support.
/// </summary>
/// <typeparam name="TViewModel">The type of view model.</typeparam>
/// <typeparam name="TModel">The type of data model.</typeparam>
public abstract class CosmosDBRepository<TViewModel, TModel> : AbstractBulkViewModelRepository<TViewModel, TModel>
    where TModel : AbstractModel
    where TViewModel : ILoadable<TModel>
{
    /// <summary>
    /// Gets the Cosmos DB store.
    /// This works with wrapped stores (e.g., tenant wrappers).
    /// </summary>
    public CosmosDBStore<TModel>? CosmosStore => Store?.GetUnwrappedStore<TModel, CosmosDBStore<TModel>>();

    /// <summary>
    /// Initializes a new instance of the CosmosDBRepository class.
    /// </summary>
    public CosmosDBRepository()
        : base(null)
    {
        Store = new CosmosDBStore<TModel>();
    }

    /// <summary>
    /// Initializes a new instance with a connection string.
    /// </summary>
    /// <param name="connectionString">The Cosmos DB connection string.</param>
    /// <param name="databaseName">The database name.</param>
    /// <param name="containerName">The container name. Defaults to the type name.</param>
    public CosmosDBRepository(string connectionString, string databaseName, string? containerName = null)
        : base(null)
    {
        Store = new CosmosDBStore<TModel>(connectionString, databaseName, containerName);
    }

    /// <summary>
    /// Initializes a new instance with an existing Cosmos DB container.
    /// </summary>
    /// <param name="container">The Cosmos DB container.</param>
    public CosmosDBRepository(Container container)
        : base(null)
    {
        Store = new CosmosDBStore<TModel>(container);
    }

    /// <summary>
    /// Initializes a new instance with an existing store.
    /// </summary>
    /// <param name="store">The Cosmos DB store to use. Can be wrapped (e.g., by tenant wrappers).</param>
    public CosmosDBRepository(IStore<TModel>? store)
        : base(null)
    {
        if (store != null && !store.IsStoreOfType<TModel, CosmosDBStore<TModel>>())
        {
            throw new ArgumentException(
                "Store must be of type CosmosDBStore<TModel> or a wrapper around it (e.g., TenantStoreWrapper).",
                nameof(store));
        }
        Store = store ?? new CosmosDBStore<TModel>();
    }

    /// <summary>
    /// Sets the connection settings.
    /// </summary>
    /// <param name="settings">The remote settings to use.</param>
    public void SetSettings(RemoteSettings settings)
    {
        if (settings != null && CosmosStore != null)
        {
            CosmosStore.SetSettings(settings);
        }
    }

    /// <summary>
    /// Checks if the Cosmos DB endpoint is healthy.
    /// </summary>
    /// <returns>True if the endpoint is reachable, false otherwise.</returns>
    public bool IsHealthy()
    {
        return CosmosStore?.IsHealthy() ?? false;
    }
}
