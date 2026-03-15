using System;
using System.Linq.Expressions;

namespace Birko.Data.Repositories
{
    #region Count Operations

    /// <summary>
    /// Defines count operations for model repositories.
    /// </summary>
    /// <typeparam name="T">The type of data model.</typeparam>
    public interface ICountRepository<T>
        where T : Models.AbstractModel
    {
        /// <summary>
        /// Counts entities matching the specified filter.
        /// </summary>
        /// <param name="filter">Optional filter expression.</param>
        /// <returns>The count of matching entities.</returns>
        long Count(Expression<Func<T, bool>>? filter = null);
    }

    #endregion

    #region Read Operations

    /// <summary>
    /// Defines read operations for model repositories.
    /// </summary>
    /// <typeparam name="T">The type of data model.</typeparam>
    public interface IReadRepository<T>
        where T : Models.AbstractModel
    {
        /// <summary>
        /// Reads a single entity by its unique identifier.
        /// </summary>
        /// <param name="guid">The unique identifier of the entity.</param>
        /// <returns>The entity if found; otherwise, null.</returns>
        T? Read(Guid guid);

        /// <summary>
        /// Reads a single entity matching the specified filter.
        /// </summary>
        /// <param name="filter">Optional filter expression.</param>
        /// <returns>The entity if found; otherwise, null.</returns>
        T? Read(Expression<Func<T, bool>>? filter = null);
    }

    #endregion

    #region Create Operations

    /// <summary>
    /// Defines create operations for model repositories.
    /// </summary>
    /// <typeparam name="T">The type of data model.</typeparam>
    public interface ICreateRepository<T>
        where T : Models.AbstractModel
    {
        /// <summary>
        /// Creates a new entity.
        /// </summary>
        /// <param name="data">The entity to create.</param>
        Guid Create(T data);
    }

    #endregion

    #region Update Operations

    /// <summary>
    /// Defines update operations for model repositories.
    /// </summary>
    /// <typeparam name="T">The type of data model.</typeparam>
    public interface IUpdateRepository<T>
        where T : Models.AbstractModel
    {
        /// <summary>
        /// Updates an existing entity.
        /// </summary>
        /// <param name="data">The entity with updated values.</param>
        void Update(T data);
    }

    #endregion

    #region Delete Operations

    /// <summary>
    /// Defines delete operations for model repositories.
    /// </summary>
    /// <typeparam name="T">The type of data model.</typeparam>
    public interface IDeleteRepository<T>
        where T : Models.AbstractModel
    {
        /// <summary>
        /// Deletes an entity.
        /// </summary>
        /// <param name="data">The entity to delete.</param>
        void Delete(T data);
    }

    #endregion

    #region Complete Repository Interface

    /// <summary>
    /// Defines operations for a repository that manages data models directly.
    /// Combines all repository interfaces into a single complete interface.
    /// </summary>
    /// <typeparam name="T">The type of data model, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public interface IRepository<T>
        : IBaseRepository
        , ICountRepository<T>
        , IReadRepository<T>
        , ICreateRepository<T>
        , IUpdateRepository<T>
        , IDeleteRepository<T>
        where T : Models.AbstractModel
    {
        /// <summary>
        /// Creates a new instance of the entity type.
        /// </summary>
        /// <returns>A new instance of type T.</returns>
        T CreateInstance();

        /// <summary>
        /// Saves an entity (creates or updates based on whether it has a GUID).
        /// </summary>
        /// <param name="data">The entity to save.</param>
        Guid Save(T data);
    }

    #endregion
}
