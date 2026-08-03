using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

using Birko.Data.Stores;

namespace Birko.Data.InMemory.Stores
{
    /// <summary>
    /// Abstract base class for asynchronous in-memory data stores with bulk operations.
    /// Backs the entity set with a thread-safe <see cref="ConcurrentDictionary{TKey, TValue}"/>.
    /// Intended for unit/integration tests, prototyping, and demos where no external
    /// persistence is required. All operations complete synchronously and are wrapped in
    /// completed tasks; cancellation is observed via the lazy-init gate in the base class.
    /// </summary>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public abstract class AbstractAsyncInMemoryStore<T> : AbstractAsyncBulkStore<T>, IAsyncAggregatableStore<T>
        where T : Models.AbstractModel
    {
        #region Fields and Properties

        /// <summary>
        /// The thread-safe in-memory store of items keyed by entity GUID.
        /// </summary>
        protected readonly ConcurrentDictionary<Guid, T> _items = new();

        /// <summary>
        /// Read-only view of the stored items, keyed by GUID. Useful for assertions in tests.
        /// </summary>
        public IReadOnlyDictionary<Guid, T> Items => _items;

        #endregion

        #region Initialization and Lifecycle

        /// <inheritdoc />
        protected override Task InitCoreAsync(CancellationToken ct = default)
        {
            // Nothing to initialize — the backing dictionary is created in the field initializer.
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public override Task DestroyAsync(CancellationToken ct = default)
        {
            _items.Clear();
            return Task.CompletedTask;
        }

        #endregion

        #region Core CRUD Operations - Single Item

        /// <inheritdoc />
        protected override Task<Guid> CreateCoreAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
        {
            if (data == null)
            {
                return Task.FromResult(Guid.Empty);
            }
            data.Guid ??= Guid.NewGuid();
            processDelegate?.Invoke(data);
            _items[data.Guid.Value] = data;
            return Task.FromResult(data.Guid.Value);
        }

        /// <inheritdoc />
        protected override Task<T?> ReadCoreAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default)
        {
            var predicate = filter?.Compile();
            return Task.FromResult(_items.Values.FirstOrDefault(x => predicate?.Invoke(x) ?? true));
        }

        /// <summary>
        /// Reads a single entity by GUID using an O(1) dictionary lookup.
        /// </summary>
        public override async Task<T?> ReadAsync(Guid guid, CancellationToken ct = default)
        {
            await EnsureInitializedAsync(ct).ConfigureAwait(false);
            if (guid != Guid.Empty && _items.TryGetValue(guid, out var value))
            {
                return value;
            }
            return null;
        }

        /// <inheritdoc />
        protected override Task UpdateCoreAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
        {
            if (data?.Guid != null && _items.ContainsKey(data.Guid.Value))
            {
                processDelegate?.Invoke(data);
                _items[data.Guid.Value] = data;
            }
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        protected override Task DeleteCoreAsync(T data, CancellationToken ct = default)
        {
            if (data?.Guid != null)
            {
                _items.TryRemove(data.Guid.Value, out _);
            }
            return Task.CompletedTask;
        }

        #endregion

        #region Query and Count Operations

        /// <inheritdoc />
        protected override Task<long> CountCoreAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default)
        {
            long count = filter == null
                ? _items.Count
                : _items.Values.Count(filter.Compile());
            return Task.FromResult(count);
        }

        #endregion

        #region Core CRUD Operations - Bulk

        /// <inheritdoc />
        protected override Task<IEnumerable<T>> ReadCoreAsync(
            Expression<Func<T, bool>>? filter = null,
            OrderBy<T>? orderBy = null,
            int? limit = null,
            int? offset = null,
            CancellationToken ct = default)
        {
            var predicate = filter?.Compile();
            IEnumerable<T> result = _items.Values;
            if (predicate != null)
            {
                result = result.Where(predicate);
            }

            result = OrderByHelper.ApplyTo(result, orderBy);

            if (offset.HasValue)
            {
                result = result.Skip(offset.Value);
            }
            if (limit.HasValue)
            {
                result = result.Take(limit.Value);
            }

            // Materialize to a stable snapshot — the backing dictionary may mutate concurrently.
            return Task.FromResult<IEnumerable<T>>(result.ToList());
        }

        /// <inheritdoc />
        protected override Task CreateCoreAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default)
        {
            if (data == null)
            {
                return Task.CompletedTask;
            }
            foreach (var item in data.Where(x => x != null))
            {
                item.Guid ??= Guid.NewGuid();
                storeDelegate?.Invoke(item);
                _items[item.Guid.Value] = item;
            }
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        protected override Task UpdateCoreAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default)
        {
            if (data == null)
            {
                return Task.CompletedTask;
            }
            foreach (var item in data.Where(x => x != null && x.Guid.HasValue && _items.ContainsKey(x.Guid.Value)))
            {
                storeDelegate?.Invoke(item);
                _items[item.Guid!.Value] = item;
            }
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        protected override Task DeleteCoreAsync(IEnumerable<T> data, CancellationToken ct = default)
        {
            if (data == null)
            {
                return Task.CompletedTask;
            }
            foreach (var item in data.Where(x => x != null && x.Guid.HasValue))
            {
                _items.TryRemove(item.Guid!.Value, out _);
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// Deletes all entities matching the filter in a single pass over the backing dictionary.
        /// </summary>
        public override async Task DeleteAsync(Expression<Func<T, bool>> filter, CancellationToken ct = default)
        {
            // SH-M023 — see AbstractInMemoryStore.Delete.
            RequireFilter(filter, "delete");
            await EnsureInitializedAsync(ct).ConfigureAwait(false);
            var predicate = filter.Compile();
            foreach (var pair in _items.Where(pair => predicate(pair.Value)).ToList())
            {
                _items.TryRemove(pair.Key, out _);
            }
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
            await EnsureInitializedAsync(ct).ConfigureAwait(false);
            return await AggregateHelper.LinqAggregateAsync(_items.Values, query, ct);
        }

        #endregion
    }
}
