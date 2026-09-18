using System;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Repositories
{
    #region Async Count Operations

    /// <summary>
    /// Defines async count operations for model repositories.
    /// </summary>
    /// <typeparam name="T">The type of data model.</typeparam>
    public interface IAsyncCountRepository<T>
        where T : Models.AbstractModel
    {
        /// <summary>
        /// Asynchronously counts entities matching the specified filter.
        /// </summary>
        /// <param name="filter">Optional filter expression.</param>
        /// <param name="ct">A cancellation token to cancel the operation.</param>
        /// <returns>The count of matching entities.</returns>
        Task<long> CountAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default);
    }

    #endregion

    #region Async Read Operations

    /// <summary>
    /// Defines async read operations for model repositories.
    /// </summary>
    /// <typeparam name="T">The type of data model.</typeparam>
    public interface IAsyncReadRepository<T>
        where T : Models.AbstractModel
    {
        /// <summary>
        /// Asynchronously reads a single entity by its unique identifier.
        /// </summary>
        /// <param name="guid">The unique identifier of the entity.</param>
        /// <param name="ct">A cancellation token to cancel the operation.</param>
        /// <returns>The entity if found; otherwise, null.</returns>
        Task<T?> ReadAsync(Guid guid, CancellationToken ct = default);

        /// <summary>
        /// Asynchronously reads a single entity matching the specified filter.
        /// </summary>
        /// <param name="filter">Optional filter expression.</param>
        /// <param name="ct">A cancellation token to cancel the operation.</param>
        /// <returns>The entity if found; otherwise, null.</returns>
        Task<T?> ReadAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default);
    }

    #endregion

    #region Async Create Operations

    /// <summary>
    /// Defines async create operations for model repositories.
    /// </summary>
    /// <typeparam name="T">The type of data model.</typeparam>
    public interface IAsyncCreateRepository<T>
        where T : Models.AbstractModel
    {
        /// <summary>
        /// Asynchronously creates a new entity.
        /// </summary>
        /// <param name="data">The entity to create.</param>
        /// <param name="ct">A cancellation token to cancel the operation.</param>
        Task<Guid> CreateAsync(T data, CancellationToken ct = default);
    }

    #endregion

    #region Async Update Operations

    /// <summary>
    /// Defines async update operations for model repositories.
    /// </summary>
    /// <typeparam name="T">The type of data model.</typeparam>
    public interface IAsyncUpdateRepository<T>
        where T : Models.AbstractModel
    {
        /// <summary>
        /// Asynchronously updates an existing entity.
        /// </summary>
        /// <param name="data">The entity with updated values.</param>
        /// <param name="ct">A cancellation token to cancel the operation.</param>
        Task UpdateAsync(T data, CancellationToken ct = default);
    }

    #endregion

    #region Async Delete Operations

    /// <summary>
    /// Defines async delete operations for model repositories.
    /// </summary>
    /// <typeparam name="T">The type of data model.</typeparam>
    public interface IAsyncDeleteRepository<T>
        where T : Models.AbstractModel
    {
        /// <summary>
        /// Asynchronously deletes an entity.
        /// </summary>
        /// <param name="data">The entity to delete.</param>
        /// <param name="ct">A cancellation token to cancel the operation.</param>
        Task DeleteAsync(T data, CancellationToken ct = default);
    }

    #endregion

    #region Complete Async Repository Interface

    /// <summary>
    /// Defines async operations for a repository that manages data models directly.
    /// Combines all async repository interfaces into a single complete interface.
    /// </summary>
    /// <typeparam name="T">The type of data model, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public interface IAsyncRepository<T>
        : IAsyncBaseRepository
        , IAsyncCountRepository<T>
        , IAsyncReadRepository<T>
        , IAsyncCreateRepository<T>
        , IAsyncUpdateRepository<T>
        , IAsyncDeleteRepository<T>
        where T : Models.AbstractModel
    {
        /// <summary>
        /// Creates a new instance of the entity type.
        /// </summary>
        /// <returns>A new instance of type T.</returns>
        T CreateInstance();

        /// <summary>
        /// Asynchronously saves an entity (creates or updates based on whether it has a GUID).
        /// </summary>
        /// <param name="data">The entity to save.</param>
        /// <param name="ct">A cancellation token to cancel the operation.</param>
        /// <returns>The unique identifier of the saved entity.</returns>
        Task<Guid> SaveAsync(T data, CancellationToken ct = default);
    }

    #endregion
}
