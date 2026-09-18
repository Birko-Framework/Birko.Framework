using Birko.Data.XML.Stores;
using Birko.Data.Repositories;
using Birko.Data.Stores;
using Birko.Configuration;
using System;

namespace Birko.Data.XML.Repositories
{
    /// <summary>
    /// Async XML repository for direct model access with bulk support.
    /// </summary>
    /// <typeparam name="T">The type of data model.</typeparam>
    public class AsyncXmlModelRepository<T> : AbstractAsyncBulkRepository<T>
        where T : Models.AbstractModel
    {
        /// <summary>
        /// Gets the async XML store.
        /// </summary>
        public AsyncXmlStore<T>? AsyncXmlStore => Store?.GetUnwrappedStore<T, AsyncXmlStore<T>>();

        public AsyncXmlModelRepository(IAsyncStore<T>? store)
            : base(null)
        {
            if (store != null && !store.IsStoreOfType<T, AsyncXmlStore<T>>())
            {
                throw new ArgumentException(
                    "Store must be of type AsyncXmlStore<T> or a wrapper around it.",
                    nameof(store));
            }
            if (store != null)
            {
                Store = store;
            }
        }
    }
}
