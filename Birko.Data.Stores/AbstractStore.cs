using System;
using System.Linq.Expressions;

namespace Birko.Data.Stores
{
    /// <summary>
    /// Abstract base class for synchronous data stores.
    /// Provides core CRUD and lifecycle operations for entities.
    /// Automatically initializes on first CRUD operation via lazy-init.
    /// </summary>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public abstract class AbstractStore<T> : IStore<T>
         where T : Models.AbstractModel
    {
        private bool _initialized;
        private readonly object _initLock = new();

        #region Initialization and Lifecycle

        /// <summary>
        /// Ensures the store is initialized. Called automatically before CRUD operations.
        /// Uses double-checked locking for thread safety.
        /// </summary>
        protected void EnsureInitialized()
        {
            if (_initialized) return;
            lock (_initLock)
            {
                if (_initialized) return;
                InitCore();
                _initialized = true;
            }
        }

        /// <inheritdoc />
        public void Init()
        {
            EnsureInitialized();
        }

        /// <summary>
        /// Core initialization logic. Override to set up storage backend (create tables, indexes, etc.).
        /// Called once automatically before the first CRUD operation, or explicitly via Init.
        /// </summary>
        protected abstract void InitCore();

        /// <inheritdoc />
        public abstract void Destroy();

        #endregion

        #region Core CRUD Operations - Single Item

        /// <inheritdoc />
        public virtual Guid Create(T data, StoreDataDelegate<T>? storeDelegate = null)
        {
            EnsureInitialized();
            return CreateCore(data, storeDelegate);
        }

        /// <summary>
        /// Core create implementation. Override in concrete stores.
        /// </summary>
        protected abstract Guid CreateCore(T data, StoreDataDelegate<T>? storeDelegate = null);

        /// <inheritdoc />
        public virtual T? Read(Expression<Func<T, bool>>? filter = null)
        {
            EnsureInitialized();
            return ReadCore(filter);
        }

        /// <summary>
        /// Core read implementation. Override in concrete stores.
        /// </summary>
        protected abstract T? ReadCore(Expression<Func<T, bool>>? filter = null);

        /// <inheritdoc />
        public virtual T? Read(Guid guid)
        {
            return Read((new Filters.ModelByGuid<T>(guid)).Filter());
        }

        /// <inheritdoc />
        public virtual void Update(T data, StoreDataDelegate<T>? storeDelegate = null)
        {
            EnsureInitialized();
            UpdateCore(data, storeDelegate);
        }

        /// <summary>
        /// Core update implementation. Override in concrete stores.
        /// </summary>
        protected abstract void UpdateCore(T data, StoreDataDelegate<T>? storeDelegate = null);

        /// <inheritdoc />
        public virtual void Delete(T data)
        {
            EnsureInitialized();
            DeleteCore(data);
        }

        /// <summary>
        /// Core delete implementation. Override in concrete stores.
        /// </summary>
        protected abstract void DeleteCore(T data);

        #endregion

        #region Query and Count Operations

        /// <inheritdoc />
        public virtual long Count(Expression<Func<T, bool>>? filter = null)
        {
            EnsureInitialized();
            return CountCore(filter);
        }

        /// <summary>
        /// Core count implementation. Override in concrete stores.
        /// </summary>
        protected abstract long CountCore(Expression<Func<T, bool>>? filter = null);

        #endregion

        #region Utility Methods

        /// <summary>
        /// Saves an entity, deciding between create and update based on whether it has a GUID.
        /// </summary>
        /// <param name="data">The entity to save.</param>
        /// <param name="storeDelegate">Optional delegate to process the entity before saving.</param>
        public virtual Guid Save(T data, StoreDataDelegate<T>? storeDelegate = null)
        {
            if (data == null)
            {
                return Guid.Empty;
            }
            if (data.Guid == null || data.Guid == Guid.Empty)
            {
                return Create(data, storeDelegate);
            }
            else
            {
                Update(data, storeDelegate);
                return data.Guid!.Value;
            }
        }

        /// <summary>
        /// Creates a new instance of the entity type.
        /// </summary>
        /// <returns>A new instance of type T.</returns>
        public virtual T CreateInstance()
        {
            try
            {
                return Activator.CreateInstance<T>();
            }
            catch (MissingMethodException)
            {
                return (T)Activator.CreateInstance(typeof(T), Array.Empty<object>())!;
            }
        }

        #endregion
    }
}
