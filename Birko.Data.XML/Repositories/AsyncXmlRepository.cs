using Birko.Data.XML.Stores;
using Birko.Data.Stores;
using Birko.Configuration;
using System;

namespace Birko.Data.XML.Repositories
{
    /// <summary>
    /// Async XML repository with bulk operations support.
    /// Uses AsyncXmlStore which includes all bulk operations functionality.
    /// </summary>
    /// <typeparam name="TViewModel">The type of view model.</typeparam>
    /// <typeparam name="TModel">The type of data model.</typeparam>
    public class AsyncXmlRepository<TViewModel, TModel> : AbstractAsyncBulkViewModelRepository<TViewModel, TModel>
        where TModel : Models.AbstractModel, Models.ILoadable<TViewModel>
        where TViewModel : Models.ILoadable<TModel>
    {
        #region Properties

        /// <summary>
        /// Gets the async XML store.
        /// This works with wrapped stores (e.g., tenant wrappers).
        /// </summary>
        public AsyncXmlStore<TModel>? AsyncXmlStore => Store?.GetUnwrappedStore<TModel, AsyncXmlStore<TModel>>();

        #endregion

        #region Constructors and Initialization

        /// <summary>
        /// Initializes a new instance with an async XML store.
        /// </summary>
        /// <param name="store">The async XML store to use. Can be wrapped (e.g., by tenant wrappers).</param>
        /// <exception cref="ArgumentException">Thrown when store is not a AsyncXmlStore or wrapper around it.</exception>
        public AsyncXmlRepository(IAsyncStore<TModel>? store)
                : base(null)
        {
            if (store != null && !store.IsStoreOfType<TModel, AsyncXmlStore<TModel>>())
            {
                throw new ArgumentException(
                    "Store must be of type AsyncXmlStore<TModel> or a wrapper around it (e.g., TenantStoreWrapper).",
                    nameof(store));
            }
            // Set the store after validation - base constructor handles null by creating default
            if (store != null)
            {
                Store = store;
            }
        }

        #endregion
    }
}
