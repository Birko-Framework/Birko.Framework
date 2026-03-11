using Birko.Data.Stores;
using global::MongoDB.Driver;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.MongoDB.Repositories
{
    /// <summary>
    /// Async MongoDB repository for direct model access with bulk support.
    /// </summary>
    /// <typeparam name="T">The type of data model.</typeparam>
    public class AsyncMongoDBModelRepository<T>
        : Data.Repositories.AbstractAsyncBulkRepository<T>
        where T : Data.Models.AbstractModel
    {
        /// <summary>
        /// Gets the MongoDB async store.
        /// </summary>
        public Stores.AsyncMongoDBStore<T>? MongoDBStore => Store?.GetUnwrappedStore<T, Stores.AsyncMongoDBStore<T>>();

        public AsyncMongoDBModelRepository()
            : base(null)
        {
            Store = new Stores.AsyncMongoDBStore<T>();
        }

        public AsyncMongoDBModelRepository(Data.Stores.IAsyncStore<T>? store)
            : base(null)
        {
            if (store != null && !store.IsStoreOfType<T, Stores.AsyncMongoDBStore<T>>())
            {
                throw new ArgumentException(
                    "Store must be of type AsyncMongoDBStore<T> or a wrapper around it.",
                    nameof(store));
            }
            Store = store ?? new Stores.AsyncMongoDBStore<T>();
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

        public async Task DropAsync(CancellationToken ct = default)
        {
            if (MongoDBStore != null)
            {
                await MongoDBStore.DestroyAsync(ct);
            }
        }

        public async Task CreateIndexAsync(IndexKeysDefinition<T> indexKeysDefinition, CancellationToken ct = default)
        {
            if (MongoDBStore?.Client != null)
            {
                await Task.Run(() => MongoDBStore.Client.CreateIndex(indexKeysDefinition), ct);
            }
        }

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
