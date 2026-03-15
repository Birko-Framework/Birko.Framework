using System;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Stores
{
    /// <summary>
    /// Provides a base implementation for async data stores.
    /// Subclasses must implement core CRUD operations for their specific storage backend.
    /// </summary>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public abstract class AbstractAsyncStore<T> : IAsyncStore<T>
        where T : Models.AbstractModel
    {
        #region Constructors and Initialization

        /// <summary>
        /// Initializes a new instance of the AbstractAsyncStore class.
        /// </summary>
        public AbstractAsyncStore()
        {
        }

        /// <inheritdoc />
        public abstract Task InitAsync(CancellationToken ct = default);

        /// <inheritdoc />
        public abstract Task DestroyAsync(CancellationToken ct = default);

        #endregion

        #region Core CRUD Operations - Single Item

        /// <inheritdoc />
        public abstract Task<Guid> CreateAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default);

        /// <inheritdoc />
        public virtual Task<T?> ReadAsync(Guid guid, CancellationToken ct = default)
        {
            return ReadAsync(x => x.Guid == guid, ct);
        }

        /// <inheritdoc />
        public abstract Task<T?> ReadAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default);

        /// <inheritdoc />
        public abstract Task UpdateAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default);

        /// <inheritdoc />
        public abstract Task DeleteAsync(T data, CancellationToken ct = default);

        #endregion

        #region Query and Count Operations

        /// <inheritdoc />
        public abstract Task<long> CountAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default);

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
