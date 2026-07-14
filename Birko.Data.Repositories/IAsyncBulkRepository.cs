using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Repositories
{
    #region Async Bulk Read Operations

    /// <summary>
    /// Defines async bulk read operations for model repositories.
    /// </summary>
    /// <typeparam name="T">The type of data model.</typeparam>
    public interface IAsyncBulkReadRepository<T> : IAsyncReadRepository<T>
        where T : Models.AbstractModel
    {
        /// <summary>
        /// Asynchronously reads all entities.
        /// </summary>
        /// <param name="ct">A cancellation token to cancel the operation.</param>
        /// <returns>A collection of all entities.</returns>
        Task<IEnumerable<T>> ReadAsync(CancellationToken ct = default);

        /// <summary>
        /// Asynchronously reads entities matching the specified filter with optional sorting and pagination.
        /// </summary>
        /// <param name="filter">Optional filter expression.</param>
        /// <param name="orderBy">Optional sort specification.</param>
        /// <param name="limit">Maximum number of entities to return.</param>
        /// <param name="offset">Number of entities to skip.</param>
        /// <param name="ct">A cancellation token to cancel the operation.</param>
        /// <returns>A collection of matching entities.</returns>
        Task<IEnumerable<T>> ReadAsync(Expression<Func<T, bool>>? filter = null, Stores.OrderBy<T>? orderBy = null, int? limit = null, int? offset = null, CancellationToken ct = default);

        /// <summary>
        /// Asynchronously reads the first entity matching the filter. Provided for parity with the store
        /// contract's <see cref="Stores.IAsyncBulkReadStore{T}.ReadFirstAsync"/> — on a bulk repository the
        /// inherited <c>ReadAsync(filter)</c> returns the collection, so this is the single-result accessor.
        /// </summary>
        /// <param name="filter">Optional filter expression.</param>
        /// <param name="ct">A cancellation token to cancel the operation.</param>
        /// <returns>The first matching entity, or null.</returns>
        Task<T?> ReadFirstAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default);
    }

    #endregion

    #region Async Bulk Create Operations

    /// <summary>
    /// Defines async bulk create operations for model repositories.
    /// </summary>
    /// <typeparam name="T">The type of data model.</typeparam>
    public interface IAsyncBulkCreateRepository<T> : IAsyncCreateRepository<T>
        where T : Models.AbstractModel
    {
        /// <summary>
        /// Asynchronously creates multiple entities.
        /// </summary>
        /// <param name="data">The entities to create.</param>
        /// <param name="ct">A cancellation token to cancel the operation.</param>
        Task CreateAsync(IEnumerable<T> data, CancellationToken ct = default);
    }

    #endregion

    #region Async Bulk Update Operations

    /// <summary>
    /// Defines async bulk update operations for model repositories.
    /// </summary>
    /// <typeparam name="T">The type of data model.</typeparam>
    public interface IAsyncBulkUpdateRepository<T> : IAsyncUpdateRepository<T>
        where T : Models.AbstractModel
    {
        /// <summary>
        /// Asynchronously updates multiple entities.
        /// </summary>
        /// <param name="data">The entities with updated values.</param>
        /// <param name="ct">A cancellation token to cancel the operation.</param>
        Task UpdateAsync(IEnumerable<T> data, CancellationToken ct = default);

        /// <summary>
        /// Asynchronously updates all entities matching the filter by applying the specified action.
        /// </summary>
        /// <param name="filter">Filter expression to select entities to update.</param>
        /// <param name="updateAction">Action to apply to each matching entity.</param>
        /// <param name="ct">A cancellation token to cancel the operation.</param>
        Task UpdateAsync(Expression<Func<T, bool>> filter, Action<T> updateAction, CancellationToken ct = default);

        /// <summary>
        /// Asynchronously updates specific properties on all entities matching the filter.
        /// </summary>
        /// <param name="filter">Filter expression to select entities to update.</param>
        /// <param name="updates">Property assignments to apply.</param>
        /// <param name="ct">A cancellation token to cancel the operation.</param>
        Task UpdateAsync(Expression<Func<T, bool>> filter, Stores.PropertyUpdate<T> updates, CancellationToken ct = default);
    }

    #endregion

    #region Async Bulk Delete Operations

    /// <summary>
    /// Defines async bulk delete operations for model repositories.
    /// </summary>
    /// <typeparam name="T">The type of data model.</typeparam>
    public interface IAsyncBulkDeleteRepository<T> : IAsyncDeleteRepository<T>
        where T : Models.AbstractModel
    {
        /// <summary>
        /// Asynchronously deletes multiple entities.
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

    #region Complete Async Bulk Repository Interface

    /// <summary>
    /// Defines async bulk operations for a model repository.
    /// Combines all async repository interfaces with bulk operation capabilities.
    /// </summary>
    /// <typeparam name="T">The type of data model, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public interface IAsyncBulkRepository<T>
        : IAsyncRepository<T>
        , IAsyncBulkReadRepository<T>
        , IAsyncBulkCreateRepository<T>
        , IAsyncBulkUpdateRepository<T>
        , IAsyncBulkDeleteRepository<T>
        where T : Models.AbstractModel
    {
    }

    #endregion
}
