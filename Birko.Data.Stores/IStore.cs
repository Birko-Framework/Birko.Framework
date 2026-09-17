using System;
using System.Linq.Expressions;
using Birko.Configuration;

namespace Birko.Data.Stores
{
    #region Delegate

    /// <summary>
    /// Delegate for processing store data during operations.
    /// </summary>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public delegate T StoreDataDelegate<T>(T data) where T : Models.AbstractModel;

    #endregion

    #region Base Store Interface

    /// <summary>
    /// Defines the base lifecycle operations for stores.
    /// </summary>
    public interface IBaseStore
    {
        /// <summary>
        /// Initializes the store.
        /// </summary>
        void Init();

        /// <summary>
        /// <b>PERMANENTLY DELETES every row this store can see.</b> This is not disposal.
        /// </summary>
        /// <remarks>
        /// <para>
        /// SH-H046. This used to read <i>"Destroys the store and releases all resources"</i>, which
        /// describes disposal -- and worse, describes what <see cref="System.IDisposable.Dispose"/>
        /// already does on the stores that hold resources (<c>RavenDBStore</c>, <c>InfluxDBStore</c>,
        /// <c>DataBaseStore</c> and their async twins all implement it). A consumer reaching for
        /// cleanup therefore had no reason to look past this method, and a teardown-style call
        /// destroys production data.
        /// </para>
        /// <para>
        /// <b>The blast radius is per backend and is wider than this entity on two of them:</b>
        /// RavenDB sends <c>DeleteDatabasesOperation(hardDelete: true)</c> and drops the
        /// <b>entire database</b>; CosmosDB deletes the container; MongoDB drops the collection;
        /// SQL drops the table; JSON and XML delete the file; InMemory clears its dictionary.
        /// Nothing is recoverable and nothing is scoped to a filter.
        /// </para>
        /// <para>
        /// <b>To release resources, use <see cref="System.IDisposable"/> where the store implements
        /// it</b> -- not this method. To delete rows selectively use
        /// <c>IBulkDeleteStore&lt;T&gt;.Delete(filter)</c>; to empty a store while keeping it usable
        /// use <c>DeleteAll()</c> on <c>AbstractBulkStore&lt;T&gt;</c> (a base-class member, not on
        /// this interface). This method exists for dropping a store outright (schema teardown,
        /// test fixtures, the <c>BackgroundJobs.*JobQueueSchema.DropAsync</c> helpers) and is named
        /// for what it destroys rather than for the caller's intent, per CLAUDE.md § Conventions.
        /// </para>
        /// </remarks>
        void Destroy();
    }

    #endregion

    #region Settings Store Interface

    /// <summary>
    /// Defines settings configuration for stores.
    /// </summary>
    /// <typeparam name="TSettings">The type of settings.</typeparam>
    public interface ISettingsStore<TSettings>
    {
        /// <summary>
        /// Sets the configuration settings for the store.
        /// </summary>
        /// <param name="settings">The settings to apply.</param>
        void SetSettings(TSettings settings);
    }

    #endregion

    #region Count Operations

    /// <summary>
    /// Defines count operations for stores.
    /// </summary>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public interface ICountStore<T>
         where T : Models.AbstractModel
    {
        /// <summary>
        /// Counts the total number of entities.
        /// </summary>
        /// <param name="filter">Optional filter expression to count matching entities.</param>
        /// <returns>The total count of entities.</returns>
        long Count(Expression<Func<T, bool>>? filter = null);
    }

    #endregion

    #region Read Operations

    /// <summary>
    /// Defines read operations for stores.
    /// </summary>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public interface IReadStore<T>
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
        /// <param name="filter">A predicate expression to filter the entity.</param>
        /// <returns>The entity if found; otherwise, null.</returns>
        T? Read(Expression<Func<T, bool>>? filter = null);
    }

    #endregion

    #region Create Operations

    /// <summary>
    /// Defines create operations for stores.
    /// </summary>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public interface ICreateStore<T>
         where T : Models.AbstractModel
    {
        /// <summary>
        /// Creates a new entity in the store.
        /// </summary>
        /// <param name="data">The entity to create.</param>
        /// <param name="storeDelegate">Optional delegate to process the entity before creation.</param>
        Guid Create(T data, StoreDataDelegate<T>? storeDelegate = null);
    }

    #endregion

    #region Update Operations

    /// <summary>
    /// Defines update operations for stores.
    /// </summary>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public interface IUpdateStore<T>
         where T : Models.AbstractModel
    {
        /// <summary>
        /// Updates an existing entity in the store.
        /// </summary>
        /// <param name="data">The entity with updated values.</param>
        /// <param name="storeDelegate">Optional delegate to process the entity before update.</param>
        void Update(T data, StoreDataDelegate<T>? storeDelegate = null);
    }

    #endregion

    #region Delete Operations

    /// <summary>
    /// Defines delete operations for stores.
    /// </summary>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public interface IDeleteStore<T>
        where T : Models.AbstractModel
    {
        /// <summary>
        /// Deletes a single entity from the store.
        /// </summary>
        /// <param name="data">The entity to delete.</param>
        void Delete(T data);
    }

    #endregion

    #region Complete Store Interface

    /// <summary>
    /// Defines operations for a data store that manages CRUD operations on entities.
    /// Combines all store interfaces into a single complete interface.
    /// </summary>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public interface IStore<T>
        : IBaseStore
        , ICountStore<T>
        , IReadStore<T>
        , ICreateStore<T>
        , IUpdateStore<T>
        , IDeleteStore<T>
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
        /// <param name="storeDelegate">Optional delegate to process the entity before saving.</param>
        Guid Save(T data, StoreDataDelegate<T>? storeDelegate = null);
    }

    #endregion

    #region Settings Store with Store Operations

    /// <summary>
    /// Combines store operations with settings configuration.
    /// </summary>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    /// <typeparam name="TSettings">The type of settings.</typeparam>
    public interface ISettingsStore<T, TSettings>
        : IStore<T>
        , ISettingsStore<TSettings>
        where T : Models.AbstractModel
        where TSettings : ISettings
    {
    }

    #endregion
}
