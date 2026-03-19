using Birko.Data.JSON.Stores;
using Birko.Data.Repositories;
using Birko.Data.Stores;
using Birko.Configuration;
using System;

namespace Birko.Data.JSON.Repositories
{
    /// <summary>
    /// JSON repository for direct model access with bulk support.
    /// </summary>
    /// <typeparam name="T">The type of data model.</typeparam>
    public class JsonModelRepository<T> : AbstractBulkRepository<T>
        where T : Models.AbstractModel
    {
        /// <summary>
        /// Gets the JSON store.
        /// </summary>
        public JsonStore<T>? JsonStore => Store?.GetUnwrappedStore<T, JsonStore<T>>();

        public JsonModelRepository(IStore<T>? store)
            : base(null)
        {
            if (store != null && !store.IsStoreOfType<T, JsonStore<T>>())
            {
                throw new ArgumentException(
                    "Store must be of type JsonStore<T> or a wrapper around it.",
                    nameof(store));
            }
            if (store != null)
            {
                Store = store;
            }
        }
    }
}
