using Birko.Data.Stores;
using Birko.Configuration;
using global::MongoDB.Driver;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.MongoDB.Repositories
{
    /// <summary>
    /// Async MongoDB repository with bulk operations support.
    /// Inherits from AbstractAsyncBulkViewModelRepository to provide bulk operations via MongoDB's async insertMany() and bulkWrite() APIs.
    /// </summary>
    /// <typeparam name="TViewModel">The type of view model.</typeparam>
    /// <typeparam name="TModel">The type of data model.</typeparam>
    public class AsyncMongoDBRepository<TViewModel, TModel>
        : Data.Repositories.AbstractAsyncBulkViewModelRepository<TViewModel, TModel>
        where TModel : Data.Models.AbstractModel, Data.Models.ILoadable<TViewModel>
        where TViewModel : Data.Models.ILoadable<TModel>
    {
        /// <summary>
        /// Gets the MongoDB async store with bulk operations support.
        /// This works with wrapped stores (e.g., tenant wrappers).
        /// </summary>
        public Stores.AsyncMongoDBStore<TModel>? MongoDBStore => Store?.GetUnwrappedStore<TModel, Stores.AsyncMongoDBStore<TModel>>();

        /// <summary>
        /// Initializes a new instance of the AsyncMongoDBRepository class.
        /// </summary>
        public AsyncMongoDBRepository()
            : base(null)
        {
            Store = new Stores.AsyncMongoDBStore<TModel>();
        }

        /// <summary>
        /// Initializes a new instance with dependency injection support.
        /// </summary>
        /// <param name="store">The async MongoDB store to use for both regular and bulk operations (optional). Can be wrapped (e.g., by tenant wrappers).</param>
        public AsyncMongoDBRepository(Data.Stores.IAsyncStore<TModel>? store)
            : base(null)
        {
            if (store != null && !store.IsStoreOfType<TModel, Stores.AsyncMongoDBStore<TModel>>())
            {
                throw new ArgumentException(
                    "Store must be of type AsyncMongoDBStore<TModel> or a wrapper around it (e.g., AsyncTenantStoreWrapper).",
                    nameof(store));
            }
            Store = store ?? new Stores.AsyncMongoDBStore<TModel>();
        }

        /// <summary>
        /// Sets the connection settings.
        /// </summary>
        /// <param name="settings">The MongoDB settings to use.</param>
        public void SetSettings(MongoDB.Stores.Settings settings)
        {
            if (settings != null && MongoDBStore != null)
            {
                MongoDBStore.SetSettings(settings);
            }
        }

        /// <summary>
        /// Checks if the MongoDB server is healthy.
        /// </summary>
        /// <returns>True if the server is reachable, false otherwise.</returns>
        public bool IsHealthy()
        {
            return MongoDBStore?.Client?.IsHealthy() ?? false;
        }

        /// <summary>
        /// Drops the MongoDB collection for this repository.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        public async Task DropAsync(CancellationToken ct = default)
        {
            if (MongoDBStore != null)
            {
                await MongoDBStore.DestroyAsync(ct);
            }
        }

        /// <summary>
        /// Creates an index on the MongoDB collection.
        /// </summary>
        /// <param name="indexKeysDefinition">The index keys definition.</param>
        /// <param name="ct">Cancellation token.</param>
        public async Task CreateIndexAsync(IndexKeysDefinition<TModel> indexKeysDefinition, CancellationToken ct = default)
        {
            if (MongoDBStore?.Client != null)
            {
                await Task.Run(() => MongoDBStore.Client.CreateIndex(indexKeysDefinition), ct);
            }
        }

        /// <inheritdoc />
        public override async Task DestroyAsync(CancellationToken ct = default)
        {
            await base.DestroyAsync(ct);
            if (MongoDBStore != null)
            {
                await DropAsync(ct);
            }
        }
    }
}
