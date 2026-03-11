using Birko.Data.Stores;
using System;

namespace Birko.Data.JSON.Repositories
{
    /// <summary>
    /// Async JSON repository for direct model access with bulk support.
    /// </summary>
    /// <typeparam name="T">The type of data model.</typeparam>
    public class AsyncJsonModelRepository<T> : Birko.Data.Repositories.AbstractAsyncBulkRepository<T>
        where T : Data.Models.AbstractModel
    {
        /// <summary>
        /// Gets the async JSON store.
        /// </summary>
        public AsyncJsonStore<T>? JsonStore => Store?.GetUnwrappedStore<T, AsyncJsonStore<T>>();

        public AsyncJsonModelRepository(Birko.Data.Stores.IAsyncStore<T>? store)
            : base(null)
        {
            if (store != null && !store.IsStoreOfType<T, AsyncJsonStore<T>>())
            {
                throw new ArgumentException(
                    "Store must be of type AsyncJsonStore<T> or a wrapper around it.",
                    nameof(store));
            }
            if (store != null)
            {
                Store = store;
            }
        }
    }
}
