using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Repositories
{
    #region Async Bulk Read Operations

    /// <summary>
    /// Defines async bulk read operations for repositories.
    /// </summary>
    /// <typeparam name="TViewModel">The type of view model.</typeparam>
    /// <typeparam name="TModel">The type of data model.</typeparam>
    public interface IAsyncBulkViewModelReadRepository<TViewModel, TModel> : IAsyncViewModelReadRepository<TViewModel, TModel>
        where TModel : Models.AbstractModel, Models.ILoadable<TViewModel>
        where TViewModel : Models.ILoadable<TModel>
    {
        /// <summary>
        /// Asynchronously reads entities with optional filtering, limit, and offset.
        /// </summary>
        /// <param name="filter">Optional filter expression.</param>
        /// <param name="limit">Optional maximum number of results.</param>
        /// <param name="offset">Optional offset for pagination.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>An async enumerable of view models.</returns>
        IAsyncEnumerable<TViewModel> ReadAsync(
            Expression<Func<TModel, bool>>? filter = null,
            int? limit = null,
            int? offset = null,
            CancellationToken ct = default);
    }

    #endregion

    #region Async Bulk Create Operations

    /// <summary>
    /// Defines async bulk create operations for repositories.
    /// </summary>
    /// <typeparam name="TViewModel">The type of view model.</typeparam>
    /// <typeparam name="TModel">The type of data model.</typeparam>
    public interface IAsyncBulkViewModelCreateRepository<TViewModel, TModel> : IAsyncViewModelCreateRepository<TViewModel, TModel>
        where TModel : Models.AbstractModel, Models.ILoadable<TViewModel>
        where TViewModel : Models.ILoadable<TModel>
    {
        /// <summary>
        /// Asynchronously creates multiple entities.
        /// </summary>
        /// <param name="data">The entities to create.</param>
        /// <param name="storeDelegate">Optional callback to process each item before creation.</param>
        /// <param name="ct">Cancellation token.</param>
        Task CreateAsync(IEnumerable<TViewModel> data, Stores.StoreDataDelegate<TModel>? storeDelegate = null, CancellationToken ct = default);
    }

    #endregion

    #region Async Bulk Update Operations

    /// <summary>
    /// Defines async bulk update operations for repositories.
    /// </summary>
    /// <typeparam name="TViewModel">The type of view model.</typeparam>
    /// <typeparam name="TModel">The type of data model.</typeparam>
    public interface IAsyncBulkViewModelUpdateRepository<TViewModel, TModel> : IAsyncViewModelUpdateRepository<TViewModel, TModel>
        where TModel : Models.AbstractModel, Models.ILoadable<TViewModel>
        where TViewModel : Models.ILoadable<TModel>
    {
        /// <summary>
        /// Asynchronously updates multiple entities.
        /// </summary>
        /// <param name="data">The entities to update.</param>
        /// <param name="storeDelegate">Optional callback to process each item before update.</param>
        /// <param name="ct">Cancellation token.</param>
        Task UpdateAsync(IEnumerable<TViewModel> data, Stores.StoreDataDelegate<TModel>? storeDelegate = null, CancellationToken ct = default);

        /// <summary>
        /// Asynchronously updates all entities matching the filter by applying the specified action.
        /// </summary>
        /// <param name="filter">Filter expression to select entities to update.</param>
        /// <param name="updateAction">Action to apply to each matching entity.</param>
        /// <param name="ct">Cancellation token.</param>
        Task UpdateAsync(Expression<Func<TModel, bool>> filter, Action<TModel> updateAction, CancellationToken ct = default);

        /// <summary>
        /// Asynchronously updates specific properties on all entities matching the filter.
        /// </summary>
        /// <param name="filter">Filter expression to select entities to update.</param>
        /// <param name="updates">Property assignments to apply.</param>
        /// <param name="ct">Cancellation token.</param>
        Task UpdateAsync(Expression<Func<TModel, bool>> filter, Stores.PropertyUpdate<TModel> updates, CancellationToken ct = default);
    }

    #endregion

    #region Async Bulk Delete Operations

    /// <summary>
    /// Defines async bulk delete operations for repositories.
    /// </summary>
    /// <typeparam name="TViewModel">The type of view model.</typeparam>
    /// <typeparam name="TModel">The type of data model.</typeparam>
    public interface IAsyncBulkViewModelDeleteRepository<TViewModel, TModel> : IAsyncViewModelDeleteRepository<TViewModel, TModel>
        where TModel : Models.AbstractModel, Models.ILoadable<TViewModel>
        where TViewModel : Models.ILoadable<TModel>
    {
        /// <summary>
        /// Asynchronously deletes multiple entities.
        /// </summary>
        /// <param name="data">The entities to delete.</param>
        /// <param name="ct">Cancellation token.</param>
        Task DeleteAsync(IEnumerable<TViewModel> data, CancellationToken ct = default);

        /// <summary>
        /// Asynchronously deletes all entities matching the specified filter.
        /// </summary>
        /// <param name="filter">Filter expression to select entities to delete.</param>
        /// <param name="ct">Cancellation token.</param>
        Task DeleteAsync(Expression<Func<TModel, bool>> filter, CancellationToken ct = default);
    }

    #endregion

    #region Complete Async Bulk Repository Interface

    /// <summary>
    /// Defines bulk operations for an async repository with ViewModel/Model separation.
    /// Combines all async repository interfaces with bulk operation capabilities.
    /// </summary>
    /// <typeparam name="TViewModel">The type of view model.</typeparam>
    /// <typeparam name="TModel">The type of data model.</typeparam>
    public interface IAsyncBulkViewModelRepository<TViewModel, TModel>
        : IAsyncViewModelRepository<TViewModel, TModel>
        , IAsyncBulkViewModelReadRepository<TViewModel, TModel>
        , IAsyncBulkViewModelCreateRepository<TViewModel, TModel>
        , IAsyncBulkViewModelUpdateRepository<TViewModel, TModel>
        , IAsyncBulkViewModelDeleteRepository<TViewModel, TModel>
        where TModel : Models.AbstractModel, Models.ILoadable<TViewModel>
        where TViewModel : Models.ILoadable<TModel>
    {
    }

    #endregion
}
