using Birko.Data.Filters;
using System.Collections.Generic;

namespace Birko.Data.Repositories
{
    #region Bulk Read Operations

    /// <summary>
    /// Defines bulk read operations for repositories.
    /// </summary>
    /// <typeparam name="T">The type of view model.</typeparam>
    /// <typeparam name="TModel">The type of data model.</typeparam>
    public interface IBulkViewModelReadRepository<T, TModel> : IViewModelReadRepository<T, TModel>
        where T : Models.ILoadable<TModel>
        where TModel : Models.AbstractModel
    {
        /// <summary>
        /// Reads entities matching the specified filter with optional pagination.
        /// </summary>
        /// <param name="filter">Optional filter to apply to the query.</param>
        /// <param name="limit">Maximum number of entities to return.</param>
        /// <param name="offset">Number of entities to skip.</param>
        /// <returns>A collection of matching view models.</returns>
        IEnumerable<T> Read(IFilter<TModel>? filter = null, int? limit = null, int? offset = null);
    }

    #endregion

    #region Bulk Create Operations

    /// <summary>
    /// Defines bulk create operations for repositories.
    /// </summary>
    /// <typeparam name="T">The type of view model.</typeparam>
    /// <typeparam name="TModel">The type of data model.</typeparam>
    public interface IBulkViewModelCreateRepository<T, TModel> : IViewModelCreateRepository<T, TModel>
        where T : Models.ILoadable<TModel>
        where TModel : Models.AbstractModel
    {
        /// <summary>
        /// Creates multiple entities based on the view models.
        /// </summary>
        /// <param name="data">The view models containing the data to create.</param>
        /// <param name="processDelegate">Optional delegate to process each data model before creation.</param>
        void Create(IEnumerable<T> data, ProcessDataDelegate<TModel>? processDelegate = null);
    }

    #endregion

    #region Bulk Update Operations

    /// <summary>
    /// Defines bulk update operations for repositories.
    /// </summary>
    /// <typeparam name="T">The type of view model.</typeparam>
    /// <typeparam name="TModel">The type of data model.</typeparam>
    public interface IBulkViewModelUpdateRepository<T, TModel> : IViewModelUpdateRepository<T, TModel>
        where T : Models.ILoadable<TModel>
        where TModel : Models.AbstractModel
    {
        /// <summary>
        /// Updates multiple entities based on the view models.
        /// </summary>
        /// <param name="data">The view models containing the updated data.</param>
        /// <param name="processDelegate">Optional delegate to process each data model before update.</param>
        void Update(IEnumerable<T> data, ProcessDataDelegate<TModel>? processDelegate = null);
    }

    #endregion

    #region Bulk Delete Operations

    /// <summary>
    /// Defines bulk delete operations for repositories.
    /// </summary>
    /// <typeparam name="T">The type of view model.</typeparam>
    /// <typeparam name="TModel">The type of data model.</typeparam>
    public interface IBulkViewModelDeleteRepository<T, TModel> : IViewModelDeleteRepository<T, TModel>
        where T : Models.ILoadable<TModel>
        where TModel : Models.AbstractModel
    {
        /// <summary>
        /// Deletes multiple entities.
        /// </summary>
        /// <param name="data">The view models representing the entities to delete.</param>
        void Delete(IEnumerable<T> data);
    }

    #endregion

    #region Complete Bulk Repository Interface

    /// <summary>
    /// Defines bulk operations for a repository.
    /// Combines all repository interfaces with bulk operation capabilities.
    /// </summary>
    /// <typeparam name="T">The type of view model, must implement <see cref="Models.ILoadable{TModel}"/>.</typeparam>
    /// <typeparam name="TModel">The type of data model, must inherit from <see cref="Models.AbstractModel"/> and implement <see cref="Models.ILoadable{T}"/>.</typeparam>
    public interface IBulkViewModelRepository<T, TModel>
         : IViewModelRepository<T, TModel>
         , IBulkViewModelReadRepository<T, TModel>
         , IBulkViewModelCreateRepository<T, TModel>
         , IBulkViewModelUpdateRepository<T, TModel>
         , IBulkViewModelDeleteRepository<T, TModel>
         where T : Models.ILoadable<TModel>
         where TModel : Models.AbstractModel, Models.ILoadable<T>
    {
    }

    #endregion
}
