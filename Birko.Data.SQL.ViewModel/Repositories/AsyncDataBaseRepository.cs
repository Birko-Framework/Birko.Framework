using System;
using Birko.Data.Repositories;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Stores;
using Birko.Data.Stores;
using Birko.Configuration;

namespace Birko.Data.SQL.Repositories
{
    /// <summary>
    /// Async database repository for SQL-based storage.
    /// Works with wrapped stores (e.g., tenant wrappers).
    /// </summary>
    /// <typeparam name="TConnector">The SQL connector type (e.g. MSSqlConnector, SqLiteConnector).</typeparam>
    /// <typeparam name="TViewModel">The type of view model.</typeparam>
    /// <typeparam name="TModel">The type of data model.</typeparam>
    /// <remarks>
    /// This must be generic over <typeparamref name="TConnector"/> (mirroring the sync
    /// <c>DataBaseRepository&lt;TConnector, TViewModel, TModel&gt;</c>): C# generics are invariant, so a
    /// concrete store such as <c>AsyncDataBaseBulkStore&lt;SqLiteConnector, TModel&gt;</c> is NOT an
    /// <c>AsyncDataBaseBulkStore&lt;AbstractConnector, TModel&gt;</c>. Hard-coding <c>AbstractConnector</c>
    /// made the type-check reject every real store (constructor threw) and the store accessor always
    /// return null (CR-C17).
    /// </remarks>
    public abstract class AsyncDataBaseRepository<TConnector, TViewModel, TModel> : Data.Repositories.AbstractAsyncBulkViewModelRepository<TViewModel, TModel>
        where TConnector : SQL.Connectors.AbstractConnector
        where TModel : Data.Models.AbstractModel
        where TViewModel : Data.Models.ILoadable<TModel>
    {
        /// <summary>
        /// Gets the database store from the (potentially wrapped) store.
        /// This works with wrapped stores (e.g., tenant wrappers).
        /// </summary>
        public AsyncDataBaseBulkStore<TConnector, TModel>? DataBaseStore =>
            Store?.GetUnwrappedStore<TModel, Stores.AsyncDataBaseBulkStore<TConnector, TModel>>();

        /// <summary>
        /// Gets the database connector from the (potentially wrapped) store.
        /// </summary>
        public TConnector? Connector => DataBaseStore?.Connector;

        /// <summary>
        /// Initializes a new instance with a default <see cref="AsyncDataBaseBulkStore{TConnector, TModel}"/>.
        /// </summary>
        public AsyncDataBaseRepository()
            : this(new AsyncDataBaseBulkStore<TConnector, TModel>())
        {
        }

        /// <summary>
        /// Initializes a new instance with dependency injection support.
        /// </summary>
        /// <param name="store">The async database bulk store to use (optional). Can be wrapped (e.g., by tenant wrappers).</param>
        public AsyncDataBaseRepository(Data.Stores.IAsyncBulkStore<TModel>? store)
            : base(null)
        {
            if (store != null && !store.IsStoreOfType<TModel, Stores.AsyncDataBaseBulkStore<TConnector, TModel>>())
            {
                throw new ArgumentException(
                    "Store must be of type AsyncDataBaseBulkStore<TConnector, TModel> or a wrapper around it (e.g., AsyncTenantBulkStoreWrapper).",
                    nameof(store));
            }
            // Set the store after validation - base constructor handles null by creating default
            if (store != null)
            {
                Store = store;
            }
        }
    }
}
