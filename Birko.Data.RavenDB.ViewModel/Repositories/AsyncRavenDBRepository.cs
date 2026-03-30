using Birko.Data.Models;
using Birko.Data.RavenDB.Stores;
using Birko.Data.Stores;
using Birko.Configuration;
using Raven.Client.Documents;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.RavenDB.Repositories;

/// <summary>
/// Async RavenDB repository with bulk operations support.
/// Inherits from AbstractAsyncBulkViewModelRepository to provide bulk operations via RavenDB's async bulk insert API.
/// </summary>
/// <typeparam name="TViewModel">The type of view model.</typeparam>
/// <typeparam name="TModel">The type of data model.</typeparam>
public abstract class AsyncRavenDBRepository<TViewModel, TModel> : Data.Repositories.AbstractAsyncBulkViewModelRepository<TViewModel, TModel>
    where TModel : AbstractModel
    where TViewModel : ILoadable<TModel>
{
    /// <summary>
    /// Gets the RavenDB async store.
    /// This works with wrapped stores (e.g., tenant wrappers).
    /// </summary>
    public AsyncRavenDBStore<TModel>? RavenDBStore => Store?.GetUnwrappedStore<TModel, AsyncRavenDBStore<TModel>>();

    /// <summary>
    /// Initializes a new instance of the AsyncRavenDBRepository class.
    /// </summary>
    public AsyncRavenDBRepository()
        : base(null)
    {
        Store = new AsyncRavenDBStore<TModel>();
    }

    /// <summary>
    /// Initializes a new instance with a connection string.
    /// </summary>
    /// <param name="connectionString">The RavenDB server URL.</param>
    /// <param name="databaseName">The database name.</param>
    public AsyncRavenDBRepository(string connectionString, string? databaseName = null)
        : base(null)
    {
        Store = new AsyncRavenDBStore<TModel>(connectionString, databaseName);
    }

    /// <summary>
    /// Initializes a new instance with an existing document store.
    /// </summary>
    /// <param name="documentStore">The RavenDB document store.</param>
    public AsyncRavenDBRepository(IDocumentStore documentStore)
        : base(null)
    {
        Store = new AsyncRavenDBStore<TModel>(documentStore);
    }

    /// <summary>
    /// Initializes a new instance with an existing store.
    /// </summary>
    /// <param name="store">The async RavenDB store to use. Can be wrapped (e.g., by tenant wrappers).</param>
    public AsyncRavenDBRepository(Data.Stores.IAsyncStore<TModel>? store)
        : base(null)
    {
        if (store != null && !store.IsStoreOfType<TModel, AsyncRavenDBStore<TModel>>())
        {
            throw new ArgumentException(
                "Store must be of type AsyncRavenDBStore<TModel> or a wrapper around it (e.g., AsyncTenantStoreWrapper).",
                nameof(store));
        }
        Store = store ?? new AsyncRavenDBStore<TModel>();
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
        return RavenDBStore?.IsHealthy() ?? false;
    }

    /// <inheritdoc />
    public override async Task DestroyAsync(CancellationToken ct = default)
    {
        await base.DestroyAsync(ct);
        if (RavenDBStore != null)
        {
            await RavenDBStore.DestroyAsync(ct);
        }
    }
}
