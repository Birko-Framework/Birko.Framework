using Birko.Data.Stores;
using System;

namespace Birko.Data.MongoDB.Repositories
{
    /// <summary>
    /// Synchronous MongoDB repository for direct model access with bulk support.
    /// </summary>
    /// <typeparam name="T">The type of data model.</typeparam>
    public class MongoDBModelRepository<T>
        : Data.Repositories.AbstractBulkRepository<T>
        where T : MongoDB.Models.MongoDBModel
    {
        /// <summary>
        /// Gets the MongoDB bulk store.
        /// </summary>
        public Stores.MongoDBStore<T>? MongoDBStore => Store?.GetUnwrappedStore<T, Stores.MongoDBStore<T>>();

        public MongoDBModelRepository()
            : base(null)
        {
            Store = new Stores.MongoDBStore<T>();
        }

        public MongoDBModelRepository(Data.Stores.IStore<T>? store)
            : base(null)
        {
            if (store != null && !store.IsStoreOfType<T, Stores.MongoDBStore<T>>())
            {
                throw new ArgumentException(
                    "Store must be of type MongoDBStore<T> or a wrapper around it.",
                    nameof(store));
            }
            Store = store ?? new Stores.MongoDBStore<T>();
        }

        public void SetSettings(MongoDB.Stores.Settings settings)
        {
            if (settings != null && MongoDBStore != null)
            {
                MongoDBStore.SetSettings(settings);
            }
        }

        public bool IsHealthy()
        {
            return MongoDBStore?.Client?.IsHealthy() ?? false;
        }

        public void Drop()
        {
            MongoDBStore?.Destroy();
        }

        public override void Destroy()
        {
            base.Destroy();
            Drop();
        }
    }
}
