using Birko.Data.Stores;
using System;

namespace Birko.Data.JSON.Repositories
{
    /// <summary>
    /// Async JSON repository with bulk operations support for file-based storage.
    /// Uses AsyncJsonStore which includes all bulk operations functionality.
    /// </summary>
    /// <typeparam name="TViewModel">The type of view model.</typeparam>
    /// <typeparam name="TModel">The type of data model.</typeparam>
    public class AsyncJsonRepository<TViewModel, TModel> : Birko.Data.Repositories.AbstractAsyncBulkViewModelRepository<TViewModel, TModel>
        where TModel : Data.Models.AbstractModel, Data.Models.ILoadable<TViewModel>
        where TViewModel : Data.Models.ILoadable<TModel>
    {
        #region Properties

        /// <summary>
        /// Gets the async JSON store.
        /// This works with wrapped stores (e.g., tenant wrappers).
        /// </summary>
        public AsyncJsonStore<TModel>? JsonStore => Store?.GetUnwrappedStore<TModel, AsyncJsonStore<TModel>>();

        #endregion

        #region Constructors and Initialization

        /// <summary>
        /// Initializes a new instance with dependency injection support.
        /// </summary>
        /// <param name="store">The async JSON store to use. Can be wrapped (e.g., by tenant wrappers).</param>
        public AsyncJsonRepository(Birko.Data.Stores.IAsyncStore<TModel>? store)
            : base(null)
        {
            if (store != null && !store.IsStoreOfType<TModel, AsyncJsonStore<TModel>>())
            {
                throw new ArgumentException(
                    "Store must be of type AsyncJsonStore<TModel> or a wrapper around it (e.g., AsyncTenantStoreWrapper).",
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
