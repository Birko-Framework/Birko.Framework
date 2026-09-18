using Birko.Data.XML.Stores;
using Birko.Data.Repositories;
using Birko.Data.Stores;
using Birko.Configuration;
using System;

namespace Birko.Data.XML.Repositories
{
    /// <summary>
    /// XML repository with bulk operations support.
    /// Uses XmlStore which includes all bulk operations functionality.
    /// </summary>
    /// <typeparam name="TViewModel">The type of view model.</typeparam>
    /// <typeparam name="TModel">The type of data model.</typeparam>
    public abstract class XmlRepository<TViewModel, TModel> : AbstractBulkViewModelRepository<TViewModel, TModel>
        where TModel : Models.AbstractModel
        where TViewModel : Models.ILoadable<TModel>
    {
        #region Properties

        /// <summary>
        /// Gets the XML store.
        /// This works with wrapped stores (e.g., tenant wrappers).
        /// </summary>
        public XmlStore<TModel>? XmlStore => Store?.GetUnwrappedStore<TModel, XmlStore<TModel>>();

        #endregion

        #region Constructors and Initialization

        /// <summary>
        /// Initializes a new instance with an XML store.
        /// </summary>
        /// <param name="store">The XML store to use. Can be wrapped (e.g., by tenant wrappers).</param>
        /// <exception cref="ArgumentException">Thrown when store is not an XmlStore or wrapper around it.</exception>
        public XmlRepository(IStore<TModel>? store)
                : base(null)
        {
            // CR-L246: base(null) is required — the bulk base ctor takes IBulkStore<TModel>? but this
            // ctor accepts any IStore (incl. a tenant wrapper), so we validate then assign through the
            // IStore-typed Store property. The base never creates a default (the old comment was wrong);
            // a null store simply leaves Store unset.
            Store = ValidateStore(store);
        }

        private static IStore<TModel>? ValidateStore(IStore<TModel>? store)
        {
            if (store != null && !store.IsStoreOfType<TModel, XmlStore<TModel>>())
            {
                throw new ArgumentException(
                    "Store must be of type XmlStore<TModel> or a wrapper around it (e.g., TenantStoreWrapper).",
                    nameof(store));
            }
            return store;
        }

        #endregion
    }
}
