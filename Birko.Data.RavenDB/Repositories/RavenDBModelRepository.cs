using Birko.Data.Models;
using Birko.Data.Repositories;
using Birko.Data.RavenDB.Stores;
using Birko.Data.Stores;
using Birko.Configuration;
using Raven.Client.Documents;
using System;

namespace Birko.Data.RavenDB.Repositories;

/// <summary>
/// RavenDB repository for direct model access with bulk support.
/// </summary>
/// <typeparam name="T">The type of data model.</typeparam>
public class RavenDBModelRepository<T> : AbstractBulkRepository<T>
    where T : AbstractModel
{
    /// <summary>
    /// Gets the RavenDB store.
    /// </summary>
    public RavenDBStore<T>? RavenDBStore => Store?.GetUnwrappedStore<T, RavenDBStore<T>>();

    public RavenDBModelRepository()
        : base(null)
    {
        Store = new RavenDBStore<T>();
    }

    public RavenDBModelRepository(string connectionString, string? databaseName = null)
        : base(null)
    {
        Store = new RavenDBStore<T>(connectionString, databaseName);
    }

    public RavenDBModelRepository(IDocumentStore documentStore)
        : base(null)
    {
        Store = new RavenDBStore<T>(documentStore);
    }

    public RavenDBModelRepository(IStore<T>? store)
        : base(null)
    {
        if (store != null && !store.IsStoreOfType<T, RavenDBStore<T>>())
        {
            throw new ArgumentException(
                "Store must be of type RavenDBStore<T> or a wrapper around it.",
                nameof(store));
        }
        Store = store ?? new RavenDBStore<T>();
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
        return RavenDBStore?.DatabaseExists() ?? false;
    }
}
