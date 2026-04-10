using System;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Stores
{
    /// <summary>
    /// Provides a base implementation for async data stores.
    /// Subclasses must implement core CRUD operations for their specific storage backend.
    /// Automatically initializes on first CRUD operation via lazy-init.
    /// </summary>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public abstract class AbstractAsyncStore<T> : IAsyncStore<T>
        where T : Models.AbstractModel
    {
        private bool _initialized;
        private readonly SemaphoreSlim _initLock = new(1, 1);

        #region Constructors and Initialization

        /// <summary>
        /// Initializes a new instance of the AbstractAsyncStore class.
        /// </summary>
        public AbstractAsyncStore()
        {
        }

        /// <summary>
        /// Ensures the store is initialized. Called automatically before CRUD operations.
        /// Uses double-checked locking for thread safety.
        /// </summary>
        protected async Task EnsureInitializedAsync(CancellationToken ct = default)
        {
            if (_initialized) return;
            await _initLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_initialized) return;
                await InitCoreAsync(ct).ConfigureAwait(false);
                _initialized = true;
            }
            finally
            {
                _initLock.Release();
            }
        }

        /// <inheritdoc />
        public Task InitAsync(CancellationToken ct = default)
        {
            return EnsureInitializedAsync(ct);
        }

        /// <summary>
        /// Core initialization logic. Override to set up storage backend (create tables, indexes, etc.).
        /// Called once automatically before the first CRUD operation, or explicitly via InitAsync.
        /// </summary>
        protected abstract Task InitCoreAsync(CancellationToken ct = default);

        /// <inheritdoc />
        public abstract Task DestroyAsync(CancellationToken ct = default);

        #endregion

        #region Core CRUD Operations - Single Item

        /// <inheritdoc />
        public virtual async Task<Guid> CreateAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
        {
            await EnsureInitializedAsync(ct).ConfigureAwait(false);
            return await CreateCoreAsync(data, processDelegate, ct);
        }

        /// <summary>
        /// Core create implementation. Override in concrete stores.
        /// </summary>
        protected abstract Task<Guid> CreateCoreAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default);

        /// <inheritdoc />
        public virtual Task<T?> ReadAsync(Guid guid, CancellationToken ct = default)
        {
            return ReadAsync(x => x.Guid == guid, ct);
        }

        /// <inheritdoc />
        public virtual async Task<T?> ReadAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default)
        {
            await EnsureInitializedAsync(ct).ConfigureAwait(false);
            return await ReadCoreAsync(filter, ct);
        }

        /// <summary>
        /// Core read implementation. Override in concrete stores.
        /// </summary>
        protected abstract Task<T?> ReadCoreAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default);

        /// <inheritdoc />
        public virtual async Task UpdateAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
        {
            await EnsureInitializedAsync(ct).ConfigureAwait(false);
            await UpdateCoreAsync(data, processDelegate, ct);
        }

        /// <summary>
        /// Core update implementation. Override in concrete stores.
        /// </summary>
        protected abstract Task UpdateCoreAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default);

        /// <inheritdoc />
        public virtual async Task DeleteAsync(T data, CancellationToken ct = default)
        {
            await EnsureInitializedAsync(ct).ConfigureAwait(false);
            await DeleteCoreAsync(data, ct);
        }

        /// <summary>
        /// Core delete implementation. Override in concrete stores.
        /// </summary>
        protected abstract Task DeleteCoreAsync(T data, CancellationToken ct = default);

        #endregion

        #region Query and Count Operations

        /// <inheritdoc />
        public virtual async Task<long> CountAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default)
        {
            await EnsureInitializedAsync(ct).ConfigureAwait(false);
            return await CountCoreAsync(filter, ct);
        }

        /// <summary>
        /// Core count implementation. Override in concrete stores.
        /// </summary>
        protected abstract Task<long> CountCoreAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default);

        #endregion

        #region Utility Methods

        /// <inheritdoc />
        public virtual async Task<Guid> SaveAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
        {
            if (data == null)
            {
                return Guid.Empty;
            }

            if (data.Guid == null || data.Guid == Guid.Empty)
            {
                return await CreateAsync(data, processDelegate, ct);
            }
            else
            {
                await UpdateAsync(data, processDelegate, ct);
                return data.Guid!.Value;
            }
        }

        /// <inheritdoc />
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
