using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace Birko.Data.Repositories
{
    /// <summary>
    /// Provides a base implementation for bulk repositories that work directly with data models.
    /// Extends <see cref="AbstractRepository{T}"/> with bulk operation capabilities.
    /// </summary>
    /// <typeparam name="T">The type of data model.</typeparam>
    public abstract class AbstractBulkRepository<T>
        : AbstractRepository<T>
        , IBulkRepository<T>
        where T : Models.AbstractModel
    {
        #region Constructors

        /// <summary>
        /// Initializes a new instance with a bulk store.
        /// </summary>
        /// <param name="store">The bulk store to use for data operations.</param>
        public AbstractBulkRepository(Stores.IBulkStore<T>? store)
            : base(store)
        {
        }

        #endregion

        #region Bulk Read Operations

        /// <inheritdoc />
        public virtual IEnumerable<T> Read()
        {
            if (Store is not Stores.IBulkStore<T> bulkStore)
            {
                throw new InvalidOperationException($"Store is not type of {typeof(Stores.IBulkStore<T>)}");
            }
            return bulkStore.Read();
        }

        /// <inheritdoc />
        public virtual IEnumerable<T> Read(Expression<Func<T, bool>>? filter = null, Stores.OrderBy<T>? orderBy = null, int? limit = null, int? offset = null)
        {
            if (Store is not Stores.IBulkStore<T> bulkStore)
            {
                throw new InvalidOperationException($"Store is not type of {typeof(Stores.IBulkStore<T>)}");
            }
            return bulkStore.Read(filter, orderBy, limit, offset);
        }

        #endregion

        #region Bulk Create Operations

        /// <inheritdoc />
        public virtual void Create(IEnumerable<T> data)
        {
            if (Store is not Stores.IBulkStore<T> bulkStore)
            {
                throw new InvalidOperationException($"Store is not type of {typeof(Stores.IBulkStore<T>)}");
            }
            bulkStore.Create(data);
        }

        #endregion

        #region Bulk Update Operations

        /// <inheritdoc />
        public virtual void Update(IEnumerable<T> data)
        {
            if (Store is not Stores.IBulkStore<T> bulkStore)
            {
                throw new InvalidOperationException($"Store is not type of {typeof(Stores.IBulkStore<T>)}");
            }
            bulkStore.Update(data);
        }

        /// <inheritdoc />
        public virtual void Update(Expression<Func<T, bool>> filter, Action<T> updateAction)
        {
            if (Store is not Stores.IBulkStore<T> bulkStore)
            {
                throw new InvalidOperationException($"Store is not type of {typeof(Stores.IBulkStore<T>)}");
            }
            bulkStore.Update(filter, updateAction);
        }

        /// <inheritdoc />
        public virtual void Update(Expression<Func<T, bool>> filter, Stores.PropertyUpdate<T> updates)
        {
            if (Store is not Stores.IBulkStore<T> bulkStore)
            {
                throw new InvalidOperationException($"Store is not type of {typeof(Stores.IBulkStore<T>)}");
            }
            bulkStore.Update(filter, updates);
        }

        #endregion

        #region Bulk Delete Operations

        /// <inheritdoc />
        public virtual void Delete(IEnumerable<T> data)
        {
            if (Store is not Stores.IBulkStore<T> bulkStore)
            {
                throw new InvalidOperationException($"Store is not type of {typeof(Stores.IBulkStore<T>)}");
            }
            bulkStore.Delete(data);
        }

        /// <inheritdoc />
        public virtual void Delete(Expression<Func<T, bool>> filter)
        {
            if (Store is not Stores.IBulkStore<T> bulkStore)
            {
                throw new InvalidOperationException($"Store is not type of {typeof(Stores.IBulkStore<T>)}");
            }
            bulkStore.Delete(filter);
        }

        #endregion
    }
}
