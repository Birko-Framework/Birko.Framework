using Birko.Data.Stores;
using System;

namespace Birko.Data.Repositories
{
    /// <summary>
    /// JSON repository with bulk operations support.
    /// Uses JsonStore which includes all bulk operations functionality.
    /// </summary>
    /// <typeparam name="TViewModel">The type of view model.</typeparam>
    /// <typeparam name="TModel">The type of data model.</typeparam>
    public class JsonRepository<TViewModel, TModel> : AbstractBulkViewModelRepository<TViewModel, TModel>
        where TModel : Models.AbstractModel, Models.ILoadable<TViewModel>
        where TViewModel : Models.ILoadable<TModel>
    {
        #region Properties

        /// <summary>
        /// Gets the JSON store.
        /// This works with wrapped stores (e.g., tenant wrappers).
        /// </summary>
        public JsonStore<TModel>? JsonStore => Store?.GetUnwrappedStore<TModel, JsonStore<TModel>>();

        #endregion

        #region Constructors and Initialization

        /// <summary>
        /// Initializes a new instance with a JSON store.
        /// </summary>
        /// <param name="store">The JSON store to use. Can be wrapped (e.g., by tenant wrappers).</param>
        /// <exception cref="ArgumentException">Thrown when store is not a JsonStore or wrapper around it.</exception>
        public JsonRepository(IStore<TModel>? store)
                : base(null)
        {
            if (store != null && !store.IsStoreOfType<TModel, JsonStore<TModel>>())
            {
                throw new ArgumentException(
                    "Store must be of type JsonStore<TModel> or a wrapper around it (e.g., TenantStoreWrapper).",
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
