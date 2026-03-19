using Birko.Data.Stores;
using Birko.Configuration;
using System;

namespace Birko.Data.MongoDB.Repositories
{
    /// <summary>
    /// Synchronous MongoDB repository with bulk operations support.
    /// Inherits from AbstractBulkViewModelRepository to provide bulk operations via MongoDB's insertMany() and bulkWrite() APIs.
    /// </summary>
    /// <typeparam name="TViewModel">The type of view model.</typeparam>
    /// <typeparam name="TModel">The type of data model.</typeparam>
    public class MongoDBRepository<TViewModel, TModel>
        : Data.Repositories.AbstractBulkViewModelRepository<TViewModel, TModel>
        where TModel : MongoDB.Models.MongoDBModel, Data.Models.ILoadable<TViewModel>
        where TViewModel : Data.Models.ILoadable<TModel>
    {
        /// <summary>
        /// Gets the MongoDB bulk store.
        /// This works with wrapped stores (e.g., tenant wrappers).
        /// </summary>
        public Stores.MongoDBStore<TModel>? MongoDBStore => Store?.GetUnwrappedStore<TModel, Stores.MongoDBStore<TModel>>();

        /// <summary>
        /// Initializes a new instance of the MongoDBRepository class.
        /// </summary>
        public MongoDBRepository()
            : base(null)
        {
            Store = new Stores.MongoDBStore<TModel>();
        }

        /// <summary>
        /// Initializes a new instance with dependency injection support.
        /// </summary>
        /// <param name="store">The MongoDB bulk store to use (optional). Can be wrapped (e.g., by tenant wrappers).</param>
        public MongoDBRepository(Data.Stores.IStore<TModel>? store)
            : base(null)
        {
            if (store != null && !store.IsStoreOfType<TModel, Stores.MongoDBStore<TModel>>())
            {
                throw new ArgumentException(
                    "Store must be of type MongoDBStore<TModel> or a wrapper around it (e.g., TenantStoreWrapper).",
                    nameof(store));
            }
            Store = store ?? new Stores.MongoDBStore<TModel>();
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
        public void Drop()
        {
            MongoDBStore?.Destroy();
        }

        /// <inheritdoc />
        public override void Destroy()
        {
            base.Destroy();
            Drop();
        }
    }
}
