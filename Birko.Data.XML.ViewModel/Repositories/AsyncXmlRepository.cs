using Birko.Data.XML.Stores;
using Birko.Data.Stores;
using Birko.Configuration;
using System;

namespace Birko.Data.XML.Repositories
{
    /// <summary>
    /// Async XML repository with bulk operations support for file-based storage.
    /// Uses AsyncXmlStore which includes all bulk operations functionality.
    /// </summary>
    /// <typeparam name="TViewModel">The type of view model.</typeparam>
    /// <typeparam name="TModel">The type of data model.</typeparam>
    public abstract class AsyncXmlRepository<TViewModel, TModel> : Birko.Data.Repositories.AbstractAsyncBulkViewModelRepository<TViewModel, TModel>
        where TModel : Data.Models.AbstractModel
        where TViewModel : Data.Models.ILoadable<TModel>
    {
        #region Properties

        /// <summary>
        /// Gets the async XML store.
        /// This works with wrapped stores (e.g., tenant wrappers).
        /// </summary>
        public AsyncXmlStore<TModel>? XmlStore => Store?.GetUnwrappedStore<TModel, AsyncXmlStore<TModel>>();

        #endregion

        #region Constructors and Initialization

        /// <summary>
        /// Initializes a new instance with dependency injection support.
        /// </summary>
        /// <param name="store">The async XML store to use. Can be wrapped (e.g., by tenant wrappers).</param>
        public AsyncXmlRepository(Birko.Data.Stores.IAsyncStore<TModel>? store)
            : base(ValidateStore(store))
        {
        }

        // CR-L246: validate before the base assigns Store (the base just does Store = store; it never
        // creates a default — the old base(null) + conditional-assign dance plus its "creates default"
        // comment were misleading). A null store is allowed (leaves Store unset).
        private static Birko.Data.Stores.IAsyncStore<TModel>? ValidateStore(Birko.Data.Stores.IAsyncStore<TModel>? store)
        {
            if (store != null && !store.IsStoreOfType<TModel, AsyncXmlStore<TModel>>())
            {
                throw new ArgumentException(
                    "Store must be of type AsyncXmlStore<TModel> or a wrapper around it (e.g., AsyncTenantStoreWrapper).",
                    nameof(store));
            }
            return store;
        }

        #endregion
    }
}
