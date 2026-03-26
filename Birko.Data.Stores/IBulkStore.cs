using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace Birko.Data.Stores
{
    #region Bulk Read Operations

    /// <summary>
    /// Defines bulk read operations for stores.
    /// </summary>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public interface IBulkReadStore<T> : IReadStore<T>
         where T : Models.AbstractModel
    {
        /// <summary>
        /// Reads all entities from the store.
        /// </summary>
        /// <returns>A collection of all entities.</returns>
        IEnumerable<T> Read();

        /// <summary>
        /// Reads entities matching the specified filter with optional pagination.
        /// </summary>
        /// <param name="filter">Optional filter expression to match entities.</param>
        /// <param name="limit">Maximum number of entities to return.</param>
        /// <param name="offset">Number of entities to skip.</param>
        /// <returns>A collection of matching entities.</returns>
        IEnumerable<T> Read(Expression<Func<T, bool>>? filter = null, OrderBy<T>? orderBy = null, int? limit = null, int? offset = null);
    }

    #endregion

    #region Bulk Create Operations

    /// <summary>
    /// Defines bulk create operations for stores.
    /// </summary>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public interface IBulkCreateStore<T> : ICreateStore<T>
         where T : Models.AbstractModel
    {
        /// <summary>
        /// Creates multiple entities in the store.
        /// </summary>
        /// <param name="data">The entities to create.</param>
        /// <param name="storeDelegate">Optional delegate to process entities before creation.</param>
        void Create(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null);
    }

    #endregion

    #region Bulk Update Operations

    /// <summary>
    /// Defines bulk update operations for stores.
    /// </summary>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public interface IBulkUpdateStore<T> : IUpdateStore<T>
         where T : Models.AbstractModel
    {
        /// <summary>
        /// Updates multiple entities in the store.
        /// </summary>
        /// <param name="data">The entities with updated values.</param>
        /// <param name="storeDelegate">Optional delegate to process entities before update.</param>
        void Update(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null);

        /// <summary>
        /// Updates all entities matching the filter by applying the specified action.
        /// Uses read-modify-save pattern.
        /// </summary>
        /// <param name="filter">Filter expression to select entities to update.</param>
        /// <param name="updateAction">Action to apply to each matching entity.</param>
        void Update(Expression<Func<T, bool>> filter, Action<T> updateAction);

        /// <summary>
        /// Updates specific properties on all entities matching the filter.
        /// Platforms can translate this to native operations (e.g. SQL UPDATE SET WHERE).
        /// </summary>
        /// <param name="filter">Filter expression to select entities to update.</param>
        /// <param name="updates">Property assignments to apply.</param>
        void Update(Expression<Func<T, bool>> filter, PropertyUpdate<T> updates);
    }

    #endregion

    #region Bulk Delete Operations

    /// <summary>
    /// Defines bulk delete operations for stores.
    /// </summary>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public interface IBulkDeleteStore<T> : IDeleteStore<T>
        where T : Models.AbstractModel
    {
        /// <summary>
        /// Deletes multiple entities from the store.
        /// </summary>
        /// <param name="data">The entities to delete.</param>
        void Delete(IEnumerable<T> data);

        /// <summary>
        /// Deletes all entities matching the specified filter.
        /// </summary>
        /// <param name="filter">Filter expression to select entities to delete.</param>
        void Delete(Expression<Func<T, bool>> filter);
    }

    #endregion

    #region Complete Bulk Store Interface

    /// <summary>
    /// Defines bulk operations for a data store.
    /// Combines all store interfaces with bulk operation capabilities.
    /// </summary>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public interface IBulkStore<T>
        : IStore<T>
        , IBulkReadStore<T>
        , IBulkCreateStore<T>
        , IBulkUpdateStore<T>
        , IBulkDeleteStore<T>
        where T : Models.AbstractModel
    {
    }

    #endregion
}
