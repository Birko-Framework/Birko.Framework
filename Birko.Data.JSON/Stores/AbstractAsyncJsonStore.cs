using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

using Birko.Data.Stores;
using Birko.Configuration;
using Birko.Serialization;
using Birko.Serialization.Json;

namespace Birko.Data.JSON.Stores
{
    /// <summary>
    /// Abstract base class for async JSON file-based data stores with bulk operations.
    /// </summary>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public abstract class AbstractAsyncJsonStore<T> : AbstractAsyncBulkStore<T>, IAsyncAggregatableStore<T>
        where T : Models.AbstractModel
    {
        #region Fields and Properties

        /// <summary>
        /// The in-memory cache of items.
        /// </summary>
        protected Dictionary<Guid, T> _items = new();

        /// <summary>
        /// The JSON serializer instance.
        /// </summary>
        protected ISerializer _serializer;

        #endregion

        #region Constructors and Initialization

        /// <summary>
        /// Initializes a new instance of the AbstractAsyncJsonStore class with a JSON serializer.
        /// </summary>
        /// <param name="serializer">The JSON serializer to use. If null, creates a default SystemJsonSerializer.</param>
        protected AbstractAsyncJsonStore(ISerializer? serializer = null)
        {
            _serializer = serializer ?? new SystemJsonSerializer(
                new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                },
                new System.Text.Json.JsonWriterOptions { Indented = true });
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
            EnsureWritable();
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
            EnsureWritable();
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
            EnsureWritable();
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

        /// <summary>
        /// Refuses a write this store cannot persist. Called as the first statement of every write
        /// seam, before anything is mutated.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Measured on the shipped code: an unconfigured store accepted a create, returned a real
        /// Guid and reported <c>Count == 1</c>, and wrote nothing to disk — on every file store, both
        /// twins, both formats. The persistence methods each opened with a guard that <c>return</c>ed
        /// when there was no path or name, so the failure was reported as success. That is
        /// § Conventions' rule exactly: a write that cannot be applied must never report success.
        /// </para>
        /// <para>
        /// The default is a no-op because this base cannot see settings — they are declared one level
        /// down, in the class that owns <c>SetSettings</c>. That class supplies the check once for its
        /// whole family, so the separate- and batch-file stores below it inherit it.
        /// </para>
        /// <para>
        /// ⚠ First statement, not last. A guard that runs after the in-memory dictionary is updated
        /// still throws the right exception and still leaves the store holding a row no file backs —
        /// a caller that catches the refusal is then worse off than one that never tried.
        /// </para>
        /// </remarks>
        protected virtual void EnsureWritable()
        {
        }

        protected abstract Task LoadDataAsync(CancellationToken ct);

        /// <summary>
        /// Saves data to the JSON file asynchronously.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        protected abstract Task SaveDataAsync(CancellationToken ct);

        /// <summary>
        /// Deserializes data from a stream asynchronously using the configured serializer.
        /// </summary>
        /// <typeparam name="TData">Type of data to deserialize.</typeparam>
        /// <param name="stream">The stream to read from.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The deserialized data.</returns>
        protected async Task<TData?> ReadFromStreamAsync<TData>(Stream stream, CancellationToken ct)
        {
            return await _serializer.DeserializeAsync<TData>(stream, ct);
        }

        /// <summary>
        /// Serializes data to a stream asynchronously using the configured serializer.
        /// </summary>
        /// <typeparam name="TData">Type of data to serialize.</typeparam>
        /// <param name="stream">The stream to write to.</param>
        /// <param name="data">The data to serialize.</param>
        /// <param name="ct">Cancellation token.</param>
        protected async Task WriteToStreamAsync<TData>(Stream stream, TData data, CancellationToken ct)
        {
            await _serializer.SerializeAsync(stream, data, ct);
        }

        /// <summary>
        /// Deserializes data from a stream using the configured serializer.
        /// </summary>
        /// <typeparam name="TData">Type of data to deserialize.</typeparam>
        /// <param name="stream">The stream to read from.</param>
        /// <returns>The deserialized data.</returns>
        protected TData? ReadFromStream<TData>(Stream stream)
        {
            return _serializer.Deserialize<TData>(stream);
        }

        /// <summary>
        /// Serializes data to a stream using the configured serializer.
        /// </summary>
        /// <typeparam name="TData">Type of data to serialize.</typeparam>
        /// <param name="stream">The stream to write to.</param>
        /// <param name="data">The data to serialize.</param>
        protected void WriteToStream<TData>(Stream stream, TData data)
        {
            _serializer.Serialize(stream, data);
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
                query = OrderByHelper.ApplyTo(query, orderBy);
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

        /// <inheritdoc />
        protected override async Task CreateCoreAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default)
        {
            EnsureWritable();
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
            EnsureWritable();
            await EnsureDataLoadedAsync(ct);
            if (data == null) return;

            // CR-L129: only update items that already exist — matching single-item UpdateCoreAsync and the
            // sync bulk UpdateCore. Without the ContainsKey guard this silently upserted a non-existent
            // entity, making async-bulk Update behave as an upsert (inconsistent with Update semantics).
            var updated = false;
            foreach (var item in data.Where(x => x != null && x.Guid.HasValue))
            {
                if (!(_items?.ContainsKey(item.Guid!.Value) ?? false))
                {
                    continue;
                }
                storeDelegate?.Invoke(item);
                _items[item.Guid!.Value] = item;
                updated = true;
            }

            if (updated)
            {
                await SaveDataAsync(ct);
            }
        }

        /// <inheritdoc />
        protected override async Task DeleteCoreAsync(IEnumerable<T> data, CancellationToken ct = default)
        {
            EnsureWritable();
            await EnsureDataLoadedAsync(ct);
            if (data == null) return;

            foreach (var item in data.Where(x => x != null && x.Guid.HasValue))
            {
                _items.Remove(item.Guid!.Value);
            }

            await SaveDataAsync(ct);
        }

        #endregion

        #region Aggregation

        /// <summary>
        /// Executes an aggregation query over the in-memory items using LINQ.
        /// </summary>
        public async Task<IReadOnlyList<AggregateResult>> AggregateAsync(
            AggregateQuery<T> query,
            CancellationToken ct = default)
        {
            await EnsureDataLoadedAsync(ct);
            return await AggregateHelper.LinqAggregateAsync(_items.Values, query, ct);
        }

        #endregion
    }
}
