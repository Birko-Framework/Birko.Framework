using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Birko.Data.Stores;
using Birko.Configuration;

namespace Birko.Data.JSON.Stores
{
    /// <summary>
    /// Abstract base class for async JSON file-based data stores with bulk operations.
    /// </summary>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public abstract class AbstractAsyncJsonStore<T> : AbstractAsyncBulkStore<T>
        where T : Models.AbstractModel
    {
        #region Fields and Properties

        /// <summary>
        /// The in-memory cache of items.
        /// </summary>
        protected Dictionary<Guid, T> _items = new();

        #endregion

        #region Constructors and Initialization

        /// <summary>
        /// Initializes a new instance of the AbstractAsyncJsonStore class.
        /// </summary>
        public AbstractAsyncJsonStore()
        {
        }

        #endregion

        #region Core CRUD Operations - Single Item

        /// <inheritdoc />
        protected override async Task<T?> ReadCoreAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default)
        {
            await EnsureDataLoadedAsync(ct);
            return _items?.Values.Where(x => filter?.Compile()?.Invoke(x) ?? true)?.FirstOrDefault() ?? null;
        }

        /// <inheritdoc />
        protected override async Task<Guid> CreateCoreAsync(T data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default)
        {
            await EnsureDataLoadedAsync(ct);

            if (data == null) return Guid.Empty;

            data.Guid ??= Guid.NewGuid();
            storeDelegate?.Invoke(data);
            _items.Add(data.Guid.Value, data);

            await SaveDataAsync(ct);

            return data.Guid.Value;
        }

        /// <inheritdoc />
        protected override async Task UpdateCoreAsync(T data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default)
        {
            await EnsureDataLoadedAsync(ct);

            if (data?.Guid != null && (_items?.ContainsKey(data.Guid.Value) ?? false))
            {
                storeDelegate?.Invoke(data);
                _items[data.Guid.Value] = data;
                await SaveDataAsync(ct);
            }
        }

        /// <inheritdoc />
        protected override async Task DeleteCoreAsync(T data, CancellationToken ct = default)
        {
            await EnsureDataLoadedAsync(ct);

            if (data?.Guid != null && (_items?.ContainsKey(data.Guid.Value) ?? false))
            {
                _items.Remove(data.Guid.Value);
                await SaveDataAsync(ct);
            }
        }

        #endregion

        #region Query and Count Operations

        /// <inheritdoc />
        protected override async Task<long> CountCoreAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default)
        {
            await EnsureDataLoadedAsync(ct);
            return _items?.Where(x => filter?.Compile()?.Invoke(x.Value) ?? true)?.Count() ?? 0;
        }

        #endregion

        #region Data Persistence

        /// <summary>
        /// Ensures that data has been loaded from the file.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        protected virtual async Task EnsureDataLoadedAsync(CancellationToken ct)
        {
            if (_items == null || _items.Count == 0)
            {
                await LoadDataAsync(ct);
            }
        }

        /// <summary>
        /// Loads data from the JSON file asynchronously.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        protected abstract Task LoadDataAsync(CancellationToken ct);

        /// <summary>
        /// Saves data to the JSON file asynchronously.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        protected abstract Task SaveDataAsync(CancellationToken ct);

        /// <summary>
        /// Deserializes data from a stream asynchronously.
        /// </summary>
        /// <typeparam name="TData">Type of data to deserialize.</typeparam>
        /// <param name="stream">The stream to read from.</param>
        /// <returns>The deserialized data.</returns>
        protected static async Task<TData?> ReadFromStreamAsync<TData>(FileStream stream, CancellationToken ct)
        {
            return await JsonSerializer.DeserializeAsync<TData>(stream, cancellationToken: ct);
        }

        /// <summary>
        /// Serializes data to a stream asynchronously.
        /// </summary>
        /// <typeparam name="TData">Type of data to serialize.</typeparam>
        /// <param name="stream">The stream to write to.</param>
        /// <param name="data">The data to serialize.</param>
        protected static async Task WriteToStreamAsync<TData>(FileStream stream, TData data, CancellationToken ct)
        {
            await JsonSerializer.SerializeAsync(stream, data, cancellationToken: ct);
        }

        #endregion

        #region Core CRUD Operations - Bulk

        /// <summary>
        /// Reads all entities from JSON storage.
        /// </summary>
        public override async Task<IEnumerable<T>> ReadAsync(CancellationToken ct = default)
        {
            return await ReadAsync(null, null, null, null, ct);
        }

        /// <inheritdoc />
        protected override async Task<IEnumerable<T>> ReadCoreAsync(
            Expression<Func<T, bool>>? filter = null,
            OrderBy<T>? orderBy = null,
            int? limit = null,
            int? offset = null,
            CancellationToken ct = default)
        {
            await EnsureDataLoadedAsync(ct);
            var query = _items.Values.Where(x => filter?.Compile()?.Invoke(x) ?? true);

            if (orderBy != null && orderBy.Fields.Count > 0)
            {
                query = ApplyOrderBy(query, orderBy);
            }

            if (offset.HasValue)
            {
                query = query.Skip(offset.Value);
            }

            if (limit.HasValue)
            {
                query = query.Take(limit.Value);
            }

            return [.. query];
        }

        private IEnumerable<T> ApplyOrderBy(IEnumerable<T> source, OrderBy<T> orderBy)
        {
            IOrderedEnumerable<T>? orderedSource = null;

            foreach (var field in orderBy.Fields)
            {
                var param = Expression.Parameter(typeof(T), "x");
                var property = Expression.Property(param, field.PropertyName);
                var lambda = Expression.Lambda<Func<T, object>>(Expression.Convert(property, typeof(object)), param);
                var compiledFunc = lambda.Compile();

                if (orderedSource == null)
                {
                    orderedSource = field.Descending
                        ? source.OrderByDescending(compiledFunc)
                        : source.OrderBy(compiledFunc);
                }
                else
                {
                    orderedSource = field.Descending
                        ? orderedSource.ThenByDescending(compiledFunc)
                        : orderedSource.ThenBy(compiledFunc);
                }
            }

            return orderedSource ?? source;
        }

        /// <inheritdoc />
        protected override async Task CreateCoreAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default)
        {
            await EnsureDataLoadedAsync(ct);

            if (data == null) return;

            foreach (var item in data.Where(x => x != null))
            {
                if (!item.Guid.HasValue)
                {
                    item.Guid = Guid.NewGuid();
                }
                storeDelegate?.Invoke(item);
                _items[item.Guid!.Value] = item;
            }

            await SaveDataAsync(ct);
        }

        /// <inheritdoc />
        protected override async Task UpdateCoreAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default)
        {
            await EnsureDataLoadedAsync(ct);
            if (data == null) return;

            foreach (var item in data.Where(x => x != null && x.Guid.HasValue))
            {
                storeDelegate?.Invoke(item);
                _items[item.Guid!.Value] = item;
            }

            await SaveDataAsync(ct);
        }

        /// <inheritdoc />
        protected override async Task DeleteCoreAsync(IEnumerable<T> data, CancellationToken ct = default)
        {
            await EnsureDataLoadedAsync(ct);
            if (data == null) return;

            foreach (var item in data.Where(x => x != null && x.Guid.HasValue))
            {
                _items.Remove(item.Guid!.Value);
            }

            await SaveDataAsync(ct);
        }

        #endregion
    }
}
