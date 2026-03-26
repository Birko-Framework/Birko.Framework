using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Stores
{
    #region Async Bulk Read Operations

    /// <summary>
    /// Defines async bulk read operations for stores.
    /// </summary>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public interface IAsyncBulkReadStore<T> : IAsyncReadStore<T>
        where T : Models.AbstractModel
    {
        /// <summary>
        /// Asynchronously reads all entities from the store.
        /// </summary>
        /// <param name="ct">A cancellation token to cancel the operation.</param>
        /// <returns>A collection of all entities.</returns>
        Task<IEnumerable<T>> ReadAsync(CancellationToken ct = default);

        /// <summary>
        /// Asynchronously reads entities matching the specified filter with optional pagination.
        /// </summary>
        /// <param name="filter">Optional filter expression to match entities.</param>
        /// <param name="limit">Maximum number of entities to return.</param>
        /// <param name="offset">Number of entities to skip.</param>
        /// <param name="ct">A cancellation token to cancel the operation.</param>
        /// <returns>A collection of matching entities.</returns>
        Task<IEnumerable<T>> ReadAsync(Expression<Func<T, bool>>? filter = null, OrderBy<T>? orderBy = null, int? limit = null, int? offset = null, CancellationToken ct = default);
    }

    #endregion

    #region Async Bulk Create Operations

    /// <summary>
    /// Defines async bulk create operations for stores.
    /// </summary>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public interface IAsyncBulkCreateStore<T> : IAsyncCreateStore<T>
        where T : Models.AbstractModel
    {
        /// <summary>
        /// Asynchronously creates multiple entities in the store.
        /// </summary>
        /// <param name="data">The entities to create.</param>
        /// <param name="storeDelegate">Optional delegate to process entities before creation.</param>
        /// <param name="ct">A cancellation token to cancel the operation.</param>
        Task CreateAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default);
    }

    #endregion

    #region Async Bulk Update Operations

    /// <summary>
    /// Defines async bulk update operations for stores.
    /// </summary>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public interface IAsyncBulkUpdateStore<T> : IAsyncUpdateStore<T>
        where T : Models.AbstractModel
    {
        /// <summary>
        /// Asynchronously updates multiple entities in the store.
        /// </summary>
        /// <param name="data">The entities with updated values.</param>
        /// <param name="storeDelegate">Optional delegate to process entities before update.</param>
        /// <param name="ct">A cancellation token to cancel the operation.</param>
        Task UpdateAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default);

        /// <summary>
        /// Asynchronously updates all entities matching the filter by applying the specified action.
        /// Uses read-modify-save pattern.
        /// </summary>
        /// <param name="filter">Filter expression to select entities to update.</param>
        /// <param name="updateAction">Action to apply to each matching entity.</param>
        /// <param name="ct">A cancellation token to cancel the operation.</param>
        Task UpdateAsync(Expression<Func<T, bool>> filter, Action<T> updateAction, CancellationToken ct = default);

        /// <summary>
        /// Asynchronously updates specific properties on all entities matching the filter.
        /// Platforms can translate this to native operations (e.g. SQL UPDATE SET WHERE).
        /// </summary>
        /// <param name="filter">Filter expression to select entities to update.</param>
        /// <param name="updates">Property assignments to apply.</param>
        /// <param name="ct">A cancellation token to cancel the operation.</param>
        Task UpdateAsync(Expression<Func<T, bool>> filter, PropertyUpdate<T> updates, CancellationToken ct = default);
    }

    #endregion

    #region Async Bulk Delete Operations

    /// <summary>
    /// Defines async bulk delete operations for stores.
    /// </summary>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public interface IAsyncBulkDeleteStore<T> : IAsyncDeleteStore<T>
        where T : Models.AbstractModel
    {
        /// <summary>
        /// Asynchronously deletes multiple entities from the store.
        /// </summary>
        /// <param name="data">The entities to delete.</param>
        /// <param name="ct">A cancellation token to cancel the operation.</param>
        Task DeleteAsync(IEnumerable<T> data, CancellationToken ct = default);

        /// <summary>
        /// Asynchronously deletes all entities matching the specified filter.
        /// </summary>
        /// <param name="filter">Filter expression to select entities to delete.</param>
        /// <param name="ct">A cancellation token to cancel the operation.</param>
        Task DeleteAsync(Expression<Func<T, bool>> filter, CancellationToken ct = default);
    }

    #endregion

    #region Complete Async Bulk Store Interface

    /// <summary>
    /// Defines async bulk operations for a data store.
    /// Combines all async store interfaces with bulk operation capabilities.
    /// </summary>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public interface IAsyncBulkStore<T>
        : IAsyncStore<T>
        , IAsyncBulkReadStore<T>
        , IAsyncBulkCreateStore<T>
        , IAsyncBulkUpdateStore<T>
        , IAsyncBulkDeleteStore<T>
        where T : Models.AbstractModel
    {
    }

    #endregion
}
