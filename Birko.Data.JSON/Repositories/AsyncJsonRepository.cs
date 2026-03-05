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
    public class AsyncJsonRepository<TViewModel, TModel> : Birko.Data.Repositories.AbstractAsyncBulkRepository<TViewModel, TModel>
        where TModel : Data.Models.AbstractModel, Data.Models.ILoadable<TViewModel>
        where TViewModel : Data.Models.ILoadable<TModel>
    {
        #region Constructors and Initialization

        /// <summary>
        /// Initializes a new instance with dependency injection support.
        /// </summary>
        /// <param name="store">The async JSON store to use.</param>
        public AsyncJsonRepository(Birko.Data.Stores.IAsyncStore<TModel>? store)
            : base((AsyncJsonStore<TModel>?)store)
        {
            if (store is not null && store is not JsonStore<TModel>)
            {
                throw new ArgumentException(
                    "Store must be of type AsyncJsonStore<TModel> or null.",
                    nameof(store));
            }
        }

        #endregion
    }
}
