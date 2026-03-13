using Birko.Data.Models;
using Birko.Data.Repositories;
using Birko.Data.RavenDB.Stores;
using Birko.Data.Stores;
using Raven.Client.Documents;
using System;

namespace Birko.Data.RavenDB.Repositories;

/// <summary>
/// RavenDB repository with bulk operations support.
/// Inherits from AbstractBulkViewModelRepository to provide bulk operations via RavenDB's bulk insert API.
/// </summary>
/// <typeparam name="TViewModel">The type of view model.</typeparam>
/// <typeparam name="TModel">The type of data model.</typeparam>
public class RavenDBRepository<TViewModel, TModel> : AbstractBulkViewModelRepository<TViewModel, TModel>
    where TModel : AbstractModel, ILoadable<TViewModel>
    where TViewModel : ILoadable<TModel>
{
    /// <summary>
    /// Gets the RavenDB store.
    /// This works with wrapped stores (e.g., tenant wrappers).
    /// </summary>
    public RavenDBStore<TModel>? RavenDBStore => Store?.GetUnwrappedStore<TModel, RavenDBStore<TModel>>();

    /// <summary>
    /// Initializes a new instance of the RavenDBRepository class.
    /// </summary>
    public RavenDBRepository()
        : base(null)
    {
        Store = new RavenDBStore<TModel>();
    }

    /// <summary>
    /// Initializes a new instance with a connection string.
    /// </summary>
    /// <param name="connectionString">The RavenDB server URL.</param>
    /// <param name="databaseName">The database name.</param>
    public RavenDBRepository(string connectionString, string? databaseName = null)
        : base(null)
    {
        Store = new RavenDBStore<TModel>(connectionString, databaseName);
    }

    /// <summary>
    /// Initializes a new instance with an existing document store.
    /// </summary>
    /// <param name="documentStore">The RavenDB document store.</param>
    public RavenDBRepository(IDocumentStore documentStore)
        : base(null)
    {
        Store = new RavenDBStore<TModel>(documentStore);
    }

    /// <summary>
    /// Initializes a new instance with an existing store.
    /// </summary>
    /// <param name="store">The RavenDB store to use. Can be wrapped (e.g., by tenant wrappers).</param>
    public RavenDBRepository(IStore<TModel>? store)
        : base(null)
    {
        if (store != null && !store.IsStoreOfType<TModel, RavenDBStore<TModel>>())
        {
            throw new ArgumentException(
                "Store must be of type RavenDBStore<TModel> or a wrapper around it (e.g., TenantStoreWrapper).",
                nameof(store));
        }
        Store = store ?? new RavenDBStore<TModel>();
    }

    /// <summary>
    /// Sets the connection settings.
    /// </summary>
    /// <param name="settings">The remote settings to use.</param>
    public void SetSettings(RemoteSettings settings)
    {
        if (settings != null && RavenDBStore != null)
        {
            RavenDBStore.SetSettings(settings);
        }
    }

    /// <summary>
    /// Checks if the RavenDB server is healthy.
    /// </summary>
    /// <returns>True if the server is reachable, false otherwise.</returns>
    public bool IsHealthy()
    {
        return RavenDBStore?.DatabaseExists() ?? false;
    }
}
