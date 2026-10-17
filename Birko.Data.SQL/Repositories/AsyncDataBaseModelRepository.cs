using System;
using Birko.Data.Repositories;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Stores;
using Birko.Data.Stores;
using Birko.Configuration;

namespace Birko.Data.SQL.Repositories
{
    /// <summary>
    /// Async database repository for direct model access with SQL-based storage.
    /// </summary>
    /// <typeparam name="TConnector">The SQL connector type the store is built with.</typeparam>
    /// <typeparam name="T">The type of data model.</typeparam>
    /// <remarks>
    /// TASK-576: the connector is a type parameter, as on <see cref="DataBaseModelRepository{TConnector, T}"/>. The
    /// class used to check for <c>AsyncDataBaseBulkStore&lt;AbstractConnector, T&gt;</c>, and class generics are
    /// invariant, so it refused every store a provider builds (<c>AsyncDataBaseBulkStore&lt;SqLiteConnector, T&gt;</c>, …).
    /// </remarks>
    public class AsyncDataBaseModelRepository<TConnector, T> : Data.Repositories.AbstractAsyncBulkRepository<T>
        where TConnector : SQL.Connectors.AbstractConnector
        where T : Data.Models.AbstractModel
    {
        /// <summary>
        /// Gets the database store.
        /// </summary>
        public AsyncDataBaseBulkStore<TConnector, T>? DataBaseStore =>
            Store?.GetUnwrappedStore<T, AsyncDataBaseBulkStore<TConnector, T>>();

        /// <summary>
        /// Gets the database connector from the (potentially wrapped) store.
        /// </summary>
        public TConnector? Connector => DataBaseStore?.Connector;

        /// <summary>
        /// Initializes a new instance with dependency injection support.
        /// </summary>
        /// <param name="store">The async database bulk store to use.</param>
        public AsyncDataBaseModelRepository(Data.Stores.IAsyncBulkStore<T>? store)
            : base(null)
        {
            if (store != null && !store.IsStoreOfType<T, AsyncDataBaseBulkStore<TConnector, T>>())
            {
                throw new ArgumentException(
                    "Store must be of type AsyncDataBaseBulkStore<TConnector, T> or a wrapper around it.",
                    nameof(store));
            }
            if (store != null)
            {
                Store = store;
            }
        }

        public virtual void AddOnInit(SQL.Connectors.InitConnector onInit)
        {
            if (onInit != null)
            {
                DataBaseStore?.AddOnInit(onInit);
            }
        }

        public virtual void RemoveOnInit(SQL.Connectors.InitConnector onInit)
        {
            if (onInit != null)
            {
                DataBaseStore?.RemoveOnInit(onInit);
            }
        }
    }
}
