using System;
using Birko.Data.Filters;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Repositories
{
    #region Async Count Operations

    /// <summary>
    /// Defines async count operations for repositories.
    /// </summary>
    /// <typeparam name="TModel">The type of data model.</typeparam>
    public interface IAsyncViewModelCountRepository<TModel>
        where TModel : Models.AbstractModel
    {
        /// <summary>
        /// Asynchronously counts entities matching the specified filter.
        /// </summary>
        /// <param name="filter">Optional filter to apply to the count.</param>
        /// <param name="ct">A cancellation token to cancel the operation.</param>
        /// <returns>The count of matching entities.</returns>
        Task<long> CountAsync(IRepositoryFilter<TModel>? filter = null, CancellationToken ct = default);
    }

    #endregion

    #region Async Read Operations

    /// <summary>
    /// Defines async read operations for repositories.
    /// </summary>
    /// <typeparam name="TViewModel">The type of view model.</typeparam>
    /// <typeparam name="TModel">The type of data model.</typeparam>
    public interface IAsyncViewModelReadRepository<TViewModel, TModel>
        where TModel : Models.AbstractModel
        where TViewModel : Models.ILoadable<TModel>
    {
        /// <summary>
        /// Asynchronously reads a single view model matching the specified filter.
        /// </summary>
        /// <param name="filter">Optional filter to apply to the query.</param>
        /// <param name="ct">A cancellation token to cancel the operation.</param>
        /// <returns>The view model if found; otherwise, null.</returns>
        Task<TViewModel?> ReadAsync(IRepositoryFilter<TModel>? filter = null, CancellationToken ct = default);
    }

    #endregion

    #region Async Create Operations

    /// <summary>
    /// Defines async create operations for repositories.
    /// </summary>
    /// <typeparam name="TViewModel">The type of view model.</typeparam>
    /// <typeparam name="TModel">The type of data model.</typeparam>
    public interface IAsyncViewModelCreateRepository<TViewModel, TModel>
        where TModel : Models.AbstractModel
        where TViewModel : Models.ILoadable<TModel>
    {
        /// <summary>
        /// Asynchronously creates a new entity based on the view model.
        /// </summary>
        /// <param name="data">The view model containing the data to create.</param>
        /// <param name="processDelegate">Optional delegate to process the data model before creation.</param>
        /// <param name="ct">A cancellation token to cancel the operation.</param>
        Task<Guid> CreateAsync(TViewModel data, ProcessDataDelegate<TModel>? processDelegate = null, CancellationToken ct = default);
    }

    #endregion

    #region Async Update Operations

    /// <summary>
    /// Defines async update operations for repositories.
    /// </summary>
    /// <typeparam name="TViewModel">The type of view model.</typeparam>
    /// <typeparam name="TModel">The type of data model.</typeparam>
    public interface IAsyncViewModelUpdateRepository<TViewModel, TModel>
        where TModel : Models.AbstractModel
        where TViewModel : Models.ILoadable<TModel>
    {
        /// <summary>
        /// Asynchronously updates an existing entity based on the view model.
        /// </summary>
        /// <param name="data">The view model containing the updated data.</param>
        /// <param name="processDelegate">Optional delegate to process the data model before update.</param>
        /// <param name="ct">A cancellation token to cancel the operation.</param>
        Task UpdateAsync(TViewModel data, ProcessDataDelegate<TModel>? processDelegate = null, CancellationToken ct = default);
    }

    #endregion

    #region Async Delete Operations

    /// <summary>
    /// Defines async delete operations for repositories.
    /// </summary>
    /// <typeparam name="TViewModel">The type of view model.</typeparam>
    /// <typeparam name="TModel">The type of data model.</typeparam>
    public interface IAsyncViewModelDeleteRepository<TViewModel, TModel>
        where TModel : Models.AbstractModel
        where TViewModel : Models.ILoadable<TModel>
    {
        /// <summary>
        /// Asynchronously deletes an entity based on the view model.
        /// </summary>
        /// <param name="data">The view model representing the entity to delete.</param>
        /// <param name="ct">A cancellation token to cancel the operation.</param>
        Task DeleteAsync(TViewModel data, CancellationToken ct = default);
    }

    #endregion

    #region Complete Async Repository Interface

    /// <summary>
    /// Defines async operations for a repository that manages view models and data models.
    /// Combines all async repository interfaces into a single complete interface.
    /// </summary>
    /// <typeparam name="TViewModel">The type of view model, must implement <see cref="Models.ILoadable{TModel}"/>.</typeparam>
    /// <typeparam name="TModel">The type of data model, must inherit from <see cref="Models.AbstractModel"/> and implement <see cref="Models.ILoadable{TViewModel}"/>.</typeparam>
    public interface IAsyncViewModelRepository<TViewModel, TModel>
        : IAsyncBaseRepository
        , IAsyncViewModelCountRepository<TModel>
        , IAsyncViewModelReadRepository<TViewModel, TModel>
        , IAsyncViewModelCreateRepository<TViewModel, TModel>
        , IAsyncViewModelUpdateRepository<TViewModel, TModel>
        , IAsyncViewModelDeleteRepository<TViewModel, TModel>
        where TModel : Models.AbstractModel, Models.ILoadable<TViewModel>
        where TViewModel : Models.ILoadable<TModel>
    {
        /// <summary>
        /// Gets or sets a value indicating whether the repository is in read-only mode.
        /// </summary>
        bool ReadMode { get; set; }

        /// <summary>
        /// Creates a new instance of the view model type.
        /// </summary>
        /// <returns>A new instance of type TViewModel.</returns>
        TViewModel CreateInstance();

        /// <summary>
        /// Creates a new instance of the data model type.
        /// </summary>
        /// <returns>A new instance of type TModel.</returns>
        TModel CreateModelInstance();
    }

    #endregion
}
