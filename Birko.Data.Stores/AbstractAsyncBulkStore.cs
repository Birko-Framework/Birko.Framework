using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Stores
{
    /// <summary>
    /// Provides a base implementation for async bulk data stores.
    /// Extends <see cref="AbstractAsyncStore{T}"/> with bulk operation capabilities.
    /// </summary>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public abstract class AbstractAsyncBulkStore<T> : AbstractAsyncStore<T>, IAsyncBulkStore<T>
        where T : Models.AbstractModel
    {
        #region Constructors and Initialization

        /// <summary>
        /// Initializes a new instance of the AbstractAsyncBulkStore class.
        /// </summary>
        public AbstractAsyncBulkStore()
        {
        }

        #endregion

        #region Core CRUD Operations - Bulk

        /// <inheritdoc />
        public abstract Task CreateAsync(
            IEnumerable<T> data,
            StoreDataDelegate<T>? storeDelegate = null,
            CancellationToken ct = default);

        /// <inheritdoc />
        public abstract Task<IEnumerable<T>> ReadAsync(
            Expression<Func<T, bool>>? filter = null,
            OrderBy<T>? orderBy = null,
            int? limit = null,
            int? offset = null,
            CancellationToken ct = default);

        /// <inheritdoc />
        public virtual async Task<IEnumerable<T>> ReadAsync(CancellationToken ct = default)
        {
            return await ReadAsync(null, null, null, null, ct);
        }

        /// <inheritdoc />
        public abstract Task UpdateAsync(
            IEnumerable<T> data,
            StoreDataDelegate<T>? storeDelegate = null,
            CancellationToken ct = default);

        /// <inheritdoc />
        public virtual Task UpdateAsync(
            Expression<Func<T, bool>> filter,
            PropertyUpdate<T> updates,
            CancellationToken ct = default)
        {
            return UpdateAsync(filter, entity => updates.ApplyTo(entity), ct);
        }

        /// <inheritdoc />
        public virtual async Task UpdateAsync(
            Expression<Func<T, bool>> filter,
            Action<T> updateAction,
            CancellationToken ct = default)
        {
            var items = (await ReadAsync(filter, null, null, null, ct)).ToList();
            foreach (var item in items)
            {
                updateAction(item);
                await UpdateAsync(item, ct: ct);
            }
        }

        /// <inheritdoc />
        public abstract Task DeleteAsync(
            IEnumerable<T> data,
            CancellationToken ct = default);

        /// <inheritdoc />
        public virtual async Task DeleteAsync(
            Expression<Func<T, bool>> filter,
            CancellationToken ct = default)
        {
            var items = (await ReadAsync(filter, null, null, null, ct)).ToList();
            await DeleteAsync(items, ct);
        }

        #endregion
    }
}
