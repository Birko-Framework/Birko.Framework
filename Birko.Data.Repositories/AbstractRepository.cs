using System;
using System.Linq.Expressions;

namespace Birko.Data.Repositories
{
    /// <summary>
    /// Provides a base implementation for repositories that work directly with data models.
    /// Wraps an <see cref="Stores.IStore{T}"/> for storage operations.
    /// </summary>
    /// <typeparam name="T">The type of data model.</typeparam>
    public abstract class AbstractRepository<T>
        : IRepository<T>
        where T : Models.AbstractModel
    {
        #region Properties

        protected Stores.IStore<T>? Store { get; set; }

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance with a store.
        /// </summary>
        /// <param name="store">The store to use for data operations.</param>
        public AbstractRepository(Stores.IStore<T>? store)
        {
            Store = store;
        }

        #endregion

        #region Read Operations

        /// <inheritdoc />
        public virtual T? Read(Guid guid)
        {
            return (Store ?? throw NoStore()).Read(guid);
        }

        /// <inheritdoc />
        public virtual T? Read(Expression<Func<T, bool>>? filter = null)
        {
            return (Store ?? throw NoStore()).Read(filter);
        }

        #endregion

        #region Create Operations

        /// <inheritdoc />
        public virtual Guid Create(T data)
        {
            if (Store == null) throw NoStore();
            return Store.Create(data);
        }

        #endregion

        #region Update Operations

        /// <inheritdoc />
        public virtual void Update(T data)
        {
            (Store ?? throw NoStore()).Update(data);
        }

        #endregion

        #region Delete Operations

        /// <inheritdoc />
        public virtual void Delete(T data)
        {
            (Store ?? throw NoStore()).Delete(data);
        }

        #endregion

        #region Count Operations

        /// <inheritdoc />
        public virtual long Count(Expression<Func<T, bool>>? filter = null)
        {
            if (Store == null) throw NoStore();
            return Store.Count(filter);
        }

        #endregion

        #region Save Operations

        /// <inheritdoc />
        public virtual Guid Save(T data)
        {
            if (Store == null) throw NoStore();
            return Store.Save(data);
        }

        #endregion

        /// <summary>
        /// SH-M219 (TASK-162). A repository with no store used to answer every call as if it had worked: a create
        /// returned Guid.Empty, an update or delete did nothing, a count was 0, a read was null or empty. A mis-wired
        /// repository was indistinguishable from an empty one, so every operation now refuses.
        /// </summary>
        protected InvalidOperationException NoStore()
            => new($"{GetType().Name} has no store; construct it with one (or let its default constructor provide it).");

        #region Factory Methods

        /// <inheritdoc />
        public virtual T CreateInstance()
        {
            if (Store == null) return Activator.CreateInstance<T>();
            return Store.CreateInstance();
        }

        #endregion

        #region Lifecycle Methods

        /// <inheritdoc />
        public virtual void Destroy()
        {
            Store?.Destroy(); // no store, nothing to destroy: the one honest no-op (TASK-162)
        }

        #endregion
    }
}
