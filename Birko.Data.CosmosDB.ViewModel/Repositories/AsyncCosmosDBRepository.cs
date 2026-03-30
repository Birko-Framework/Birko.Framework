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
/// Async Cosmos DB repository with bulk operations support.
/// </summary>
/// <typeparam name="TViewModel">The type of view model.</typeparam>
/// <typeparam name="TModel">The type of data model.</typeparam>
public abstract class AsyncCosmosDBRepository<TViewModel, TModel> : Data.Repositories.AbstractAsyncBulkViewModelRepository<TViewModel, TModel>
    where TModel : AbstractModel
    where TViewModel : ILoadable<TModel>
{
    /// <summary>
    /// Gets the Cosmos DB async store.
    /// This works with wrapped stores (e.g., tenant wrappers).
    /// </summary>
    public AsyncCosmosDBStore<TModel>? CosmosStore => Store?.GetUnwrappedStore<TModel, AsyncCosmosDBStore<TModel>>();

    /// <summary>
    /// Initializes a new instance of the AsyncCosmosDBRepository class.
    /// </summary>
    public AsyncCosmosDBRepository()
        : base(null)
    {
        Store = new AsyncCosmosDBStore<TModel>();
    }

    /// <summary>
    /// Initializes a new instance with a connection string.
    /// </summary>
    /// <param name="connectionString">The Cosmos DB connection string.</param>
    /// <param name="databaseName">The database name.</param>
    /// <param name="containerName">The container name. Defaults to the type name.</param>
    public AsyncCosmosDBRepository(string connectionString, string databaseName, string? containerName = null)
        : base(null)
    {
        Store = new AsyncCosmosDBStore<TModel>(connectionString, databaseName, containerName);
    }

    /// <summary>
    /// Initializes a new instance with an existing Cosmos DB container.
    /// </summary>
    /// <param name="container">The Cosmos DB container.</param>
    public AsyncCosmosDBRepository(Container container)
        : base(null)
    {
        Store = new AsyncCosmosDBStore<TModel>(container);
    }

    /// <summary>
    /// Initializes a new instance with an existing store.
    /// </summary>
    /// <param name="store">The async Cosmos DB store to use. Can be wrapped (e.g., by tenant wrappers).</param>
    public AsyncCosmosDBRepository(Data.Stores.IAsyncStore<TModel>? store)
        : base(null)
    {
        if (store != null && !store.IsStoreOfType<TModel, AsyncCosmosDBStore<TModel>>())
        {
            throw new ArgumentException(
                "Store must be of type AsyncCosmosDBStore<TModel> or a wrapper around it (e.g., AsyncTenantStoreWrapper).",
                nameof(store));
        }
        Store = store ?? new AsyncCosmosDBStore<TModel>();
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

    /// <inheritdoc />
    public override async Task DestroyAsync(CancellationToken ct = default)
    {
        await base.DestroyAsync(ct);
        if (CosmosStore != null)
        {
            await CosmosStore.DestroyAsync(ct);
        }
    }
}
