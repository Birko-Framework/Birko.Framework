using Birko.Data.XML.Stores;
using Birko.Data.Repositories;
using Birko.Data.Stores;
using Birko.Configuration;
using System;

namespace Birko.Data.XML.Repositories
{
    /// <summary>
    /// XML repository for direct model access with bulk support.
    /// </summary>
    /// <typeparam name="T">The type of data model.</typeparam>
    public class XmlModelRepository<T> : AbstractBulkRepository<T>
        where T : Models.AbstractModel
    {
        /// <summary>
        /// Gets the XML store.
        /// </summary>
        public XmlStore<T>? XmlStore => Store?.GetUnwrappedStore<T, XmlStore<T>>();

        public XmlModelRepository(IStore<T>? store)
            : base(null)
        {
            if (store != null && !store.IsStoreOfType<T, XmlStore<T>>())
            {
                throw new ArgumentException(
                    "Store must be of type XmlStore<T> or a wrapper around it.",
                    nameof(store));
            }
            if (store != null)
            {
                Store = store;
            }
        }
    }
}
