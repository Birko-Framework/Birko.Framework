using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace Birko.Data.Repositories
{
    #region Bulk Read Operations

    /// <summary>
    /// Defines bulk read operations for model repositories.
    /// </summary>
    /// <typeparam name="T">The type of data model.</typeparam>
    public interface IBulkReadRepository<T> : IReadRepository<T>
        where T : Models.AbstractModel
    {
        /// <summary>
        /// Reads all entities.
        /// </summary>
        /// <returns>A collection of all entities.</returns>
        IEnumerable<T> Read();

        /// <summary>
        /// Reads entities matching the specified filter with optional sorting and pagination.
        /// </summary>
        /// <param name="filter">Optional filter expression.</param>
        /// <param name="orderBy">Optional sort specification.</param>
        /// <param name="limit">Maximum number of entities to return.</param>
        /// <param name="offset">Number of entities to skip.</param>
        /// <returns>A collection of matching entities.</returns>
        IEnumerable<T> Read(Expression<Func<T, bool>>? filter = null, Stores.OrderBy<T>? orderBy = null, int? limit = null, int? offset = null);
    }

    #endregion

    #region Bulk Create Operations

    /// <summary>
    /// Defines bulk create operations for model repositories.
    /// </summary>
    /// <typeparam name="T">The type of data model.</typeparam>
    public interface IBulkCreateRepository<T> : ICreateRepository<T>
        where T : Models.AbstractModel
    {
        /// <summary>
        /// Creates multiple entities.
        /// </summary>
        /// <param name="data">The entities to create.</param>
        void Create(IEnumerable<T> data);
    }

    #endregion

    #region Bulk Update Operations

    /// <summary>
    /// Defines bulk update operations for model repositories.
    /// </summary>
    /// <typeparam name="T">The type of data model.</typeparam>
    public interface IBulkUpdateRepository<T> : IUpdateRepository<T>
        where T : Models.AbstractModel
    {
        /// <summary>
        /// Updates multiple entities.
        /// </summary>
        /// <param name="data">The entities with updated values.</param>
        void Update(IEnumerable<T> data);
    }

    #endregion

    #region Bulk Delete Operations

    /// <summary>
    /// Defines bulk delete operations for model repositories.
    /// </summary>
    /// <typeparam name="T">The type of data model.</typeparam>
    public interface IBulkDeleteRepository<T> : IDeleteRepository<T>
        where T : Models.AbstractModel
    {
        /// <summary>
        /// Deletes multiple entities.
        /// </summary>
        /// <param name="data">The entities to delete.</param>
        void Delete(IEnumerable<T> data);
    }

    #endregion

    #region Complete Bulk Repository Interface

    /// <summary>
    /// Defines bulk operations for a model repository.
    /// Combines all repository interfaces with bulk operation capabilities.
    /// </summary>
    /// <typeparam name="T">The type of data model, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public interface IBulkRepository<T>
        : IRepository<T>
        , IBulkReadRepository<T>
        , IBulkCreateRepository<T>
        , IBulkUpdateRepository<T>
        , IBulkDeleteRepository<T>
        where T : Models.AbstractModel
    {
    }

    #endregion
}
