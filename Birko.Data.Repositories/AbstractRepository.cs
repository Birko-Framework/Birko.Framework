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
            return Store?.Read(guid);
        }

        /// <inheritdoc />
        public virtual T? Read(Expression<Func<T, bool>>? filter = null)
        {
            return Store?.Read(filter);
        }

        #endregion

        #region Create Operations

        /// <inheritdoc />
        public virtual Guid Create(T data)
        {
            if (Store == null) return Guid.Empty;
            return Store.Create(data);
        }

        #endregion

        #region Update Operations

        /// <inheritdoc />
        public virtual void Update(T data)
        {
            Store?.Update(data);
        }

        #endregion

        #region Delete Operations

        /// <inheritdoc />
        public virtual void Delete(T data)
        {
            Store?.Delete(data);
        }

        #endregion

        #region Count Operations

        /// <inheritdoc />
        public virtual long Count(Expression<Func<T, bool>>? filter = null)
        {
            if (Store == null) return 0;
            return Store.Count(filter);
        }

        #endregion

        #region Save Operations

        /// <inheritdoc />
        public virtual Guid Save(T data)
        {
            if (Store == null) return Guid.Empty;
            return Store.Save(data);
        }

        #endregion

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
            Store?.Destroy();
        }

        #endregion
    }
}
