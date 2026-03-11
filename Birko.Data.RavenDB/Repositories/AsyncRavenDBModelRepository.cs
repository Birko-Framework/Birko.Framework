using Birko.Data.Models;
using Birko.Data.Stores;
using Raven.Client.Documents;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.RavenDB.Repositories;

/// <summary>
/// Async RavenDB repository for direct model access with bulk support.
/// </summary>
/// <typeparam name="T">The type of data model.</typeparam>
public class AsyncRavenDBModelRepository<T> : Data.Repositories.AbstractAsyncBulkRepository<T>
    where T : AbstractModel
{
    /// <summary>
    /// Gets the RavenDB async store.
    /// </summary>
    public AsyncRavenDBStore<T>? RavenDBStore => Store?.GetUnwrappedStore<T, AsyncRavenDBStore<T>>();

    public AsyncRavenDBModelRepository()
        : base(null)
    {
        Store = new AsyncRavenDBStore<T>();
    }

    public AsyncRavenDBModelRepository(string connectionString, string? databaseName = null)
        : base(null)
    {
        Store = new AsyncRavenDBStore<T>(connectionString, databaseName);
    }

    public AsyncRavenDBModelRepository(IDocumentStore documentStore)
        : base(null)
    {
        Store = new AsyncRavenDBStore<T>(documentStore);
    }

    public AsyncRavenDBModelRepository(Data.Stores.IAsyncStore<T>? store)
        : base(null)
    {
        if (store != null && !store.IsStoreOfType<T, AsyncRavenDBStore<T>>())
        {
            throw new ArgumentException(
                "Store must be of type AsyncRavenDBStore<T> or a wrapper around it.",
                nameof(store));
        }
        Store = store ?? new AsyncRavenDBStore<T>();
    }

    public void SetSettings(RemoteSettings settings)
    {
        if (settings != null && RavenDBStore != null)
        {
            RavenDBStore.SetSettings(settings);
        }
    }

    public bool IsHealthy()
    {
        return RavenDBStore?.IsHealthy() ?? false;
    }

    public override async Task DestroyAsync(CancellationToken ct = default)
    {
        await base.DestroyAsync(ct);
        if (RavenDBStore != null)
        {
            await RavenDBStore.DestroyAsync(ct);
        }
    }
}
