using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

using Birko.Data.Stores;

namespace Birko.Data.InMemory.Stores
{
    /// <summary>
    /// Abstract base class for synchronous in-memory data stores with bulk operations.
    /// Backs the entity set with a thread-safe <see cref="ConcurrentDictionary{TKey, TValue}"/>.
    /// Intended for unit/integration tests, prototyping, and demos where no external
    /// persistence is required. All data is lost when the store is garbage-collected
    /// or <see cref="Destroy"/> is called.
    /// </summary>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public abstract class AbstractInMemoryStore<T> : AbstractBulkStore<T>, IAggregatableStore<T>
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
        protected override void InitCore()
        {
            // Nothing to initialize — the backing dictionary is created in the field initializer.
        }

        /// <inheritdoc />
        public override void Destroy()
        {
            _items.Clear();
        }

        #endregion

        #region Core CRUD Operations - Single Item

        /// <inheritdoc />
        protected override Guid CreateCore(T data, StoreDataDelegate<T>? storeDelegate = null)
        {
            if (data == null)
            {
                return Guid.Empty;
            }
            data.Guid ??= Guid.NewGuid();
            storeDelegate?.Invoke(data);
            _items[data.Guid.Value] = data;
            return data.Guid.Value;
        }

        /// <inheritdoc />
        protected override T? ReadCore(Expression<Func<T, bool>>? filter = null)
        {
            var predicate = filter?.Compile();
            return _items.Values.FirstOrDefault(x => predicate?.Invoke(x) ?? true);
        }

        /// <summary>
        /// Reads a single entity by GUID using an O(1) dictionary lookup.
        /// </summary>
        public override T? Read(Guid guid)
        {
            EnsureInitialized();
            if (guid != Guid.Empty && _items.TryGetValue(guid, out var value))
            {
                return value;
            }
            return null;
        }

        /// <inheritdoc />
        protected override void UpdateCore(T data, StoreDataDelegate<T>? storeDelegate = null)
        {
            if (data?.Guid != null && _items.ContainsKey(data.Guid.Value))
            {
                storeDelegate?.Invoke(data);
                _items[data.Guid.Value] = data;
            }
        }

        /// <inheritdoc />
        protected override void DeleteCore(T data)
        {
            if (data?.Guid != null)
            {
                _items.TryRemove(data.Guid.Value, out _);
            }
        }

        #endregion

        #region Query and Count Operations

        /// <inheritdoc />
        protected override long CountCore(Expression<Func<T, bool>>? filter = null)
        {
            if (filter == null)
            {
                return _items.Count;
            }
            return _items.Values.Count(filter.Compile());
        }

        #endregion

        #region Core CRUD Operations - Bulk

        /// <inheritdoc />
        protected override IEnumerable<T> ReadCore(Expression<Func<T, bool>>? filter = null, OrderBy<T>? orderBy = null, int? limit = null, int? offset = null)
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
            return result.ToList();
        }

        /// <inheritdoc />
        protected override void CreateCore(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null)
        {
            if (data == null)
            {
                return;
            }
            foreach (var item in data.Where(x => x != null))
            {
                item.Guid ??= Guid.NewGuid();
                storeDelegate?.Invoke(item);
                _items[item.Guid.Value] = item;
            }
        }

        /// <inheritdoc />
        protected override void UpdateCore(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null)
        {
            if (data == null)
            {
                return;
            }
            foreach (var item in data.Where(x => x != null && x.Guid.HasValue && _items.ContainsKey(x.Guid.Value)))
            {
                storeDelegate?.Invoke(item);
                _items[item.Guid!.Value] = item;
            }
        }

        /// <inheritdoc />
        protected override void DeleteCore(IEnumerable<T> data)
        {
            if (data == null)
            {
                return;
            }
            foreach (var item in data.Where(x => x != null && x.Guid.HasValue))
            {
                _items.TryRemove(item.Guid!.Value, out _);
            }
        }

        /// <summary>
        /// Deletes all entities matching the filter in a single pass over the backing dictionary.
        /// </summary>
        public override void Delete(Expression<Func<T, bool>> filter)
        {
            EnsureInitialized();
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
        public IReadOnlyList<AggregateResult> Aggregate(AggregateQuery<T> query)
        {
            EnsureInitialized();
            return AggregateHelper.LinqAggregate(_items.Values, query);
        }

        #endregion
    }
}
