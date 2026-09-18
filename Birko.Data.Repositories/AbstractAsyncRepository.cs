using System;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Repositories
{
    /// <summary>
    /// Provides a base implementation for async repositories that work directly with data models.
    /// Wraps an <see cref="Stores.IAsyncStore{T}"/> for storage operations.
    /// </summary>
    /// <typeparam name="T">The type of data model.</typeparam>
    public abstract class AbstractAsyncRepository<T>
        : IAsyncRepository<T>
        where T : Models.AbstractModel
    {
        #region Properties

        protected Stores.IAsyncStore<T>? Store { get; set; }

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance with an async store.
        /// </summary>
        /// <param name="store">The async store to use for data operations.</param>
        public AbstractAsyncRepository(Stores.IAsyncStore<T>? store)
        {
            Store = store;
        }

        #endregion

        #region Read Operations

        /// <inheritdoc />
        public virtual async Task<T?> ReadAsync(Guid guid, CancellationToken ct = default)
        {
            if (Store == null) return default;
            return await Store.ReadAsync(guid, ct);
        }

        /// <inheritdoc />
        public virtual async Task<T?> ReadAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default)
        {
            if (Store == null) return default;
            return await Store.ReadAsync(filter, ct);
        }

        #endregion

        #region Create Operations

        /// <inheritdoc />
        public virtual async Task<Guid> CreateAsync(T data, CancellationToken ct = default)
        {
            if (Store == null) return Guid.Empty;
            return await Store.CreateAsync(data, ct: ct);
        }

        #endregion

        #region Update Operations

        /// <inheritdoc />
        public virtual async Task UpdateAsync(T data, CancellationToken ct = default)
        {
            if (Store == null) return;
            await Store.UpdateAsync(data, ct: ct);
        }

        #endregion

        #region Delete Operations

        /// <inheritdoc />
        public virtual async Task DeleteAsync(T data, CancellationToken ct = default)
        {
            if (Store == null) return;
            await Store.DeleteAsync(data, ct);
        }

        #endregion

        #region Count Operations

        /// <inheritdoc />
        public virtual async Task<long> CountAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default)
        {
            if (Store == null) return 0;
            return await Store.CountAsync(filter, ct);
        }

        #endregion

        #region Save Operations

        /// <inheritdoc />
        public virtual async Task<Guid> SaveAsync(T data, CancellationToken ct = default)
        {
            if (Store == null) return Guid.Empty;
            return await Store.SaveAsync(data, ct: ct);
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
        public virtual async Task DestroyAsync(CancellationToken ct = default)
        {
            if (Store == null) return;
            await Store.DestroyAsync(ct);
        }

        #endregion
    }
}
