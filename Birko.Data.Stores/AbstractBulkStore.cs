using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Birko.Data.Stores
{
    /// <summary>
    /// Abstract base class for synchronous bulk data stores.
    /// Extends <see cref="AbstractStore{T}"/> with bulk operation capabilities.
    /// </summary>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public abstract class AbstractBulkStore<T> : AbstractStore<T>, IBulkStore<T>
         where T : Models.AbstractModel
    {
        #region Core CRUD Operations - Bulk

        /// <inheritdoc />
        public virtual void Create(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null)
        {
            EnsureInitialized();
            CreateCore(data, storeDelegate);
        }

        /// <summary>
        /// Core bulk create implementation. Override in concrete stores.
        /// </summary>
        protected abstract void CreateCore(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null);

        /// <inheritdoc />
        public virtual IEnumerable<T> Read(Expression<Func<T, bool>>? filter = null, OrderBy<T>? orderBy = null, int? limit = null, int? offset = null)
        {
            EnsureInitialized();
            return ReadCore(filter, orderBy, limit, offset);
        }

        /// <summary>
        /// Core bulk read implementation. Override in concrete stores.
        /// </summary>
        protected abstract IEnumerable<T> ReadCore(Expression<Func<T, bool>>? filter = null, OrderBy<T>? orderBy = null, int? limit = null, int? offset = null);

        /// <inheritdoc />
        public virtual IEnumerable<T> Read()
        {
            return Read(null, null, null, null);
        }

        /// <inheritdoc />
        public virtual void Update(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null)
        {
            EnsureInitialized();
            UpdateCore(data, storeDelegate);
        }

        /// <summary>
        /// Core bulk update implementation. Override in concrete stores.
        /// </summary>
        protected abstract void UpdateCore(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null);

        /// <inheritdoc />
        public virtual void Update(Expression<Func<T, bool>> filter, PropertyUpdate<T> updates)
        {
            Update(filter, entity => updates.ApplyTo(entity));
        }

        /// <inheritdoc />
        public virtual void Update(Expression<Func<T, bool>> filter, Action<T> updateAction)
        {
            var items = Read(filter, null, null, null).ToList();
            foreach (var item in items)
            {
                updateAction(item);
                Update(item);
            }
        }

        /// <inheritdoc />
        public virtual void Delete(IEnumerable<T> data)
        {
            EnsureInitialized();
            DeleteCore(data);
        }

        /// <summary>
        /// Core bulk delete implementation. Override in concrete stores.
        /// </summary>
        protected abstract void DeleteCore(IEnumerable<T> data);

        /// <inheritdoc />
        public virtual void Delete(Expression<Func<T, bool>> filter)
        {
            var items = Read(filter, null, null, null).ToList();
            Delete(items);
        }

        #endregion
    }
}
