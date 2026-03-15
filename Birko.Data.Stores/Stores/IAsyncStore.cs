using System;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Stores
{
    #region Base Async Store Interface

    /// <summary>
    /// Defines the base lifecycle operations for async stores.
    /// </summary>
    public interface IAsyncBaseStore
    {
        /// <summary>
        /// Asynchronously initializes the store.
        /// </summary>
        /// <param name="ct">A cancellation token to cancel the operation.</param>
        Task InitAsync(CancellationToken ct = default);

        /// <summary>
        /// Asynchronously destroys the store and releases all resources.
        /// </summary>
        /// <param name="ct">A cancellation token to cancel the operation.</param>
        Task DestroyAsync(CancellationToken ct = default);
    }

    #endregion

    #region Async Count Operations

    /// <summary>
    /// Defines async count operations for stores.
    /// </summary>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public interface IAsyncCountStore<T>
        where T : Models.AbstractModel
    {
        /// <summary>
        /// Asynchronously counts the total number of entities.
        /// </summary>
        /// <param name="filter">Optional filter expression to count matching entities.</param>
        /// <param name="ct">A cancellation token to cancel the operation.</param>
        /// <returns>The total count of entities.</returns>
        Task<long> CountAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default);
    }

    #endregion

    #region Async Read Operations

    /// <summary>
    /// Defines async read operations for stores.
    /// </summary>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public interface IAsyncReadStore<T>
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
        /// <param name="filter">A predicate expression to filter the entity.</param>
        /// <param name="ct">A cancellation token to cancel the operation.</param>
        /// <returns>The entity if found; otherwise, null.</returns>
        Task<T?> ReadAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default);
    }

    #endregion

    #region Async Create Operations

    /// <summary>
    /// Defines async create operations for stores.
    /// </summary>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public interface IAsyncCreateStore<T>
        where T : Models.AbstractModel
    {
        /// <summary>
        /// Asynchronously creates a new entity in the store.
        /// </summary>
        /// <param name="data">The entity to create.</param>
        /// <param name="processDelegate">Optional delegate to process the entity before creation.</param>
        /// <param name="ct">A cancellation token to cancel the operation.</param>
        /// <returns>The unique identifier of the created entity.</returns>
        Task<Guid> CreateAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default);
    }

    #endregion

    #region Async Update Operations

    /// <summary>
    /// Defines async update operations for stores.
    /// </summary>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public interface IAsyncUpdateStore<T>
        where T : Models.AbstractModel
    {
        /// <summary>
        /// Asynchronously updates an existing entity in the store.
        /// </summary>
        /// <param name="data">The entity with updated values.</param>
        /// <param name="processDelegate">Optional delegate to process the entity before update.</param>
        /// <param name="ct">A cancellation token to cancel the operation.</param>
        Task UpdateAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default);
    }

    #endregion

    #region Async Delete Operations

    /// <summary>
    /// Defines async delete operations for stores.
    /// </summary>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public interface IAsyncDeleteStore<T>
        where T : Models.AbstractModel
    {
        /// <summary>
        /// Asynchronously deletes an entity from the store.
        /// </summary>
        /// <param name="data">The entity to delete.</param>
        /// <param name="ct">A cancellation token to cancel the operation.</param>
        Task DeleteAsync(T data, CancellationToken ct = default);
    }

    #endregion

    #region Complete Async Store Interface

    /// <summary>
    /// Defines async operations for a data store that manages CRUD operations on entities.
    /// Combines all async store interfaces into a single complete interface.
    /// </summary>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public interface IAsyncStore<T>
        : IAsyncBaseStore
        , IAsyncCountStore<T>
        , IAsyncReadStore<T>
        , IAsyncCreateStore<T>
        , IAsyncUpdateStore<T>
        , IAsyncDeleteStore<T>
        where T : Models.AbstractModel
    {
        /// <summary>
        /// Asynchronously saves an entity (creates or updates based on whether it has a GUID).
        /// </summary>
        /// <param name="data">The entity to save.</param>
        /// <param name="processDelegate">Optional delegate to process the entity before saving.</param>
        /// <param name="ct">A cancellation token to cancel the operation.</param>
        /// <returns>The unique identifier of the saved entity.</returns>
        Task<Guid> SaveAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default);

        /// <summary>
        /// Creates a new instance of the entity type.
        /// </summary>
        /// <returns>A new instance of type T.</returns>
        T CreateInstance();
    }

    #endregion
}
