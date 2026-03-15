using System;
using System.Linq.Expressions;

namespace Birko.Data.Stores
{
    /// <summary>
    /// Abstract base class for synchronous data stores.
    /// Provides core CRUD and lifecycle operations for entities.
    /// </summary>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public abstract class AbstractStore<T> : IStore<T>
         where T : Models.AbstractModel
    {
        #region Initialization and Lifecycle

        /// <inheritdoc />
        public abstract void Init();

        /// <inheritdoc />
        public abstract void Destroy();

        #endregion

        #region Core CRUD Operations - Single Item

        /// <inheritdoc />
        public abstract Guid Create(T data, StoreDataDelegate<T>? storeDelegate = null);

        /// <inheritdoc />
        public abstract T? Read(Expression<Func<T, bool>>? filter = null);

        /// <inheritdoc />
        public virtual T? Read(Guid guid)
        {
            return Read((new Filters.ModelByGuid<T>(guid)).Filter());
        }

        /// <inheritdoc />
        public abstract void Update(T data, StoreDataDelegate<T>? storeDelegate = null);

        /// <inheritdoc />
        public abstract void Delete(T data);

        #endregion

        #region Query and Count Operations

        /// <inheritdoc />
        public abstract long Count(Expression<Func<T, bool>>? filter = null);

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
