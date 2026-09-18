using System;
using Birko.Data.Filters;

namespace Birko.Data.Repositories
{
    #region Count Operations

    /// <summary>
    /// Defines count operations for repositories.
    /// </summary>
    /// <typeparam name="TModel">The type of data model.</typeparam>
    public interface IViewModelCountRepository<TModel>
        where TModel : Models.AbstractModel
    {
        /// <summary>
        /// Counts the total number of entities.
        /// </summary>
        /// <param name="filter">Optional filter to apply to the count.</param>
        /// <returns>The total count of entities.</returns>
        long Count(IFilter<TModel>? filter = null);
    }

    #endregion

    #region Read Operations

    /// <summary>
    /// Defines read operations for repositories.
    /// </summary>
    /// <typeparam name="T">The type of view model.</typeparam>
    /// <typeparam name="TModel">The type of data model.</typeparam>
    public interface IViewModelReadRepository<T, TModel>
        where T : Models.ILoadable<TModel>
        where TModel : Models.AbstractModel
    {
        /// <summary>
        /// Reads a single entity matching the specified filter.
        /// </summary>
        /// <param name="filter">Optional filter to apply to the query.</param>
        /// <returns>The view model if found; otherwise, null.</returns>
        T? Read(IFilter<TModel>? filter = null);
    }

    #endregion

    #region Create Operations

    /// <summary>
    /// Defines create operations for repositories.
    /// </summary>
    /// <typeparam name="T">The type of view model.</typeparam>
    /// <typeparam name="TModel">The type of data model.</typeparam>
    public interface IViewModelCreateRepository<T, TModel>
        where TModel : Models.AbstractModel
    {
        /// <summary>
        /// Creates a new entity based on the view model.
        /// </summary>
        /// <param name="data">The view model containing the data to create.</param>
        /// <param name="processDelegate">Optional delegate to process the data model before creation.</param>
        Guid Create(T data, ProcessDataDelegate<TModel>? processDelegate = null);
    }

    #endregion

    #region Update Operations

    /// <summary>
    /// Defines update operations for repositories.
    /// </summary>
    /// <typeparam name="T">The type of view model.</typeparam>
    /// <typeparam name="TModel">The type of data model.</typeparam>
    public interface IViewModelUpdateRepository<T, TModel>
        where T : Models.ILoadable<TModel>
        where TModel : Models.AbstractModel
    {
        /// <summary>
        /// Updates an existing entity based on the view model.
        /// </summary>
        /// <param name="data">The view model containing the updated data.</param>
        /// <param name="processDelegate">Optional delegate to process the data model before update.</param>
        void Update(T data, ProcessDataDelegate<TModel>? processDelegate = null);
    }

    #endregion

    #region Delete Operations

    /// <summary>
    /// Defines delete operations for repositories.
    /// </summary>
    /// <typeparam name="T">The type of view model.</typeparam>
    public interface IViewModelDeleteRepository<T, TModel>
        where T : Models.ILoadable<TModel>
        where TModel : Models.AbstractModel
    {
        /// <summary>
        /// Deletes a single entity.
        /// </summary>
        /// <param name="data">The view model representing the entity to delete.</param>
        void Delete(T data);
    }

    #endregion

    #region Complete Repository Interface

    /// <summary>
    /// Defines operations for a repository that manages view models and data models.
    /// Combines all repository interfaces into a single complete interface.
    /// </summary>
    /// <typeparam name="T">The type of view model, must implement <see cref="Models.ILoadable{TModel}"/>.</typeparam>
    /// <typeparam name="TModel">The type of data model, must inherit from <see cref="Models.AbstractModel"/> and implement <see cref="Models.ILoadable{T}"/>.</typeparam>
    public interface IViewModelRepository<T, TModel>
        : IBaseRepository
        , IViewModelCountRepository<TModel>
        , IViewModelReadRepository<T, TModel>
        , IViewModelCreateRepository<T, TModel>
        , IViewModelUpdateRepository<T, TModel>
        , IViewModelDeleteRepository<T, TModel>

        where T : Models.ILoadable<TModel>
        where TModel : Models.AbstractModel
    {
        /// <summary>
        /// Creates a new instance of the view model type.
        /// </summary>
        /// <returns>A new instance of type T.</returns>
        T CreateInstance();

        /// <summary>
        /// Creates a new instance of the data model type.
        /// </summary>
        /// <returns>A new instance of type TModel.</returns>
        TModel CreateModelInstance();
    }

    #endregion
}
