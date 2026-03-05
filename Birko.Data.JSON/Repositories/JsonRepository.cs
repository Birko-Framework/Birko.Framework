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
    public class JsonRepository<TViewModel, TModel> : AbstractBulkRepository<TViewModel, TModel>
        where TModel : Models.AbstractModel, Models.ILoadable<TViewModel>
        where TViewModel : Models.ILoadable<TModel>
    {
        #region Constructors and Initialization

        /// <summary>
        /// Initializes a new instance with a JSON store.
        /// </summary>
        /// <param name="store">The JSON store to use.</param>
        /// <exception cref="ArgumentException">Thrown when store is not a JsonStore.</exception>
        public JsonRepository(IStore<TModel>? store)
                : base((JsonStore<TModel>?)store)
        {
            if (store is not null && store is not JsonStore<TModel>)
            {
                throw new ArgumentException(
                    "Store must be of type JsonStore<TModel> or null.",
                    nameof(store));
            }
        }

        #endregion
    }
}
