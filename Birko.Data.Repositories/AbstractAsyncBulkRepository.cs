using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Repositories
{
    /// <summary>
    /// Provides a base implementation for async bulk repositories that work directly with data models.
    /// Extends <see cref="AbstractAsyncRepository{T}"/> with bulk operation capabilities.
    /// </summary>
    /// <typeparam name="T">The type of data model.</typeparam>
    public abstract class AbstractAsyncBulkRepository<T>
        : AbstractAsyncRepository<T>
        , IAsyncBulkRepository<T>
        where T : Models.AbstractModel
    {
        #region Properties

        /// <summary>
        /// Gets the async bulk store for bulk operations.
        /// </summary>
        protected Stores.IAsyncBulkStore<T>? BulkStore => Store as Stores.IAsyncBulkStore<T>;

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance with an async bulk store.
        /// </summary>
        /// <param name="store">The async store implementing IAsyncBulkStore.</param>
        public AbstractAsyncBulkRepository(Stores.IAsyncStore<T>? store)
            : base(store)
        {
        }

        #endregion

        #region Bulk Read Operations

        /// <inheritdoc />
        public virtual async Task<IEnumerable<T>> ReadAsync(CancellationToken ct = default)
        {
            if (BulkStore == null) return Array.Empty<T>();
            return await BulkStore.ReadAsync(ct);
        }

        /// <inheritdoc />
        public virtual async Task<IEnumerable<T>> ReadAsync(Expression<Func<T, bool>>? filter = null, Stores.OrderBy<T>? orderBy = null, int? limit = null, int? offset = null, CancellationToken ct = default)
        {
            if (BulkStore == null) return Array.Empty<T>();
            return await BulkStore.ReadAsync(filter, orderBy, limit, offset, ct);
        }

        #endregion

        #region Bulk Create Operations

        /// <inheritdoc />
        public virtual async Task CreateAsync(IEnumerable<T> data, CancellationToken ct = default)
        {
            if (BulkStore == null) return;
            await BulkStore.CreateAsync(data, ct: ct);
        }

        #endregion

        #region Bulk Update Operations

        /// <inheritdoc />
        public virtual async Task UpdateAsync(IEnumerable<T> data, CancellationToken ct = default)
        {
            if (BulkStore == null) return;
            await BulkStore.UpdateAsync(data, ct: ct);
        }

        #endregion

        #region Bulk Delete Operations

        /// <inheritdoc />
        public virtual async Task DeleteAsync(IEnumerable<T> data, CancellationToken ct = default)
        {
            if (BulkStore == null) return;
            await BulkStore.DeleteAsync(data, ct);
        }

        #endregion

        #region Lifecycle Methods

        /// <inheritdoc />
        public override async Task DestroyAsync(CancellationToken ct = default)
        {
            await base.DestroyAsync(ct);
            if (BulkStore == null) return;
            await BulkStore.DestroyAsync(ct);
        }

        #endregion
    }
}
