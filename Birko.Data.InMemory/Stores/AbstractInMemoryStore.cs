using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

using Birko.Data.Exceptions;
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

        /// <summary>
        /// SH-M311 (TASK-557): store and hand out COPIES, as every serializing backend does. The store used to keep the
        /// caller's own reference and return it from every read, so <c>store.Create(e); e.Name = "x";</c> changed stored
        /// state with no <c>Update</c> — the canonical test double was false-green for the whole class of missing-Update
        /// bugs. Default <c>true</c>; set <c>false</c> only for a model that cannot round-trip through
        /// <see cref="Snapshot"/> (and accept that the double then shares references).
        /// </summary>
        public bool SnapshotEntities { get; set; } = true;

        private static readonly System.Text.Json.JsonSerializerOptions SnapshotOptions = new()
        {
            IncludeFields = true,
        };

        /// <summary>
        /// A detached copy of <paramref name="item"/> — by default a JSON round-trip, which is what a file or document
        /// backend stores. Override for a model that needs a different copy.
        /// </summary>
        protected virtual T Snapshot(T item)
        {
            if (!SnapshotEntities || item == null)
            {
                return item!;
            }
            var json = System.Text.Json.JsonSerializer.Serialize(item, item.GetType(), SnapshotOptions);
            return (T)System.Text.Json.JsonSerializer.Deserialize(json, item.GetType(), SnapshotOptions)!;
        }

        #endregion

        #region Initialization and Lifecycle

        /// <inheritdoc />
        protected override void InitCore()
        {
            // Nothing to initialize — the backing dictionary is created in the field initializer.
        }

        /// <inheritdoc />
        protected override void DestroyCore()
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
            data.EnsureGuid();
            RequireNewKeys(new[] { data });
            data = StoreDelegates.Apply(storeDelegate, data);
            AddNew(data);
            return data.Guid.Value;
        }

        /// <inheritdoc />
        protected override T? ReadCore(Expression<Func<T, bool>>? filter = null)
        {
            var predicate = filter?.Compile();
            var found = _items.Values.FirstOrDefault(x => predicate?.Invoke(x) ?? true);
            return found == null ? null : Snapshot(found);
        }

        /// <summary>
        /// Reads a single entity by GUID using an O(1) dictionary lookup.
        /// </summary>
        public override T? Read(Guid guid)
        {
            EnsureInitialized();
            if (guid != Guid.Empty && _items.TryGetValue(guid, out var value))
            {
                return Snapshot(value);
            }
            return null;
        }

        /// <inheritdoc />
        protected override void UpdateCore(T data, StoreDataDelegate<T>? storeDelegate = null)
        {
            if (data == null)
            {
                return;
            }
            // SH-M306 (TASK-555): an update of an entity that is not stored throws instead of writing nothing.
            if (data.Guid == null || !_items.ContainsKey(data.Guid.Value))
            {
                throw new EntityNotFoundException(typeof(T), data.Guid);
            }
            data = StoreDelegates.Apply(storeDelegate, data);
            if (!ReplaceExisting(data))
            {
                throw new EntityNotFoundException(typeof(T), data.Guid);
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

            // Materialize to a stable snapshot — the backing dictionary may mutate concurrently — of detached copies.
            return result.Select(Snapshot).ToList();
        }

        /// <inheritdoc />
        protected override void CreateCore(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null)
        {
            if (data == null)
            {
                return;
            }
            var items = data.Where(x => x != null).ToList();
            foreach (var item in items)
            {
                item.EnsureGuid();
            }
            RequireNewKeys(items);
            for (var itemIndex = 0; itemIndex < items.Count; itemIndex++)
            {
                var item = items[itemIndex];
                item = StoreDelegates.Apply(storeDelegate, item);
                items[itemIndex] = item; // the list is what gets persisted
                AddNew(item);
            }
        }

        /// <inheritdoc />
        protected override void UpdateCore(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null)
        {
            if (data == null)
            {
                return;
            }
            // TASK-555: an entity that is not stored is reported, not skipped — the stored ones are still written.
            var missing = new List<Guid>();
            foreach (var itemSource in data.Where(x => x != null))
            {
                var item = itemSource;
                if (!item.Guid.HasValue || !_items.ContainsKey(item.Guid.Value))
                {
                    missing.Add(item.Guid ?? Guid.Empty);
                    continue;
                }
                item = StoreDelegates.Apply(storeDelegate, item);
                if (!ReplaceExisting(item))
                {
                    missing.Add(item.Guid.Value);
                }
            }
            if (missing.Count > 0)
            {
                throw new EntityNotFoundException(typeof(T), missing);
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

        #endregion

        #region Write helpers

        /// <summary>
        /// SH-M310: a create never replaces an entity — a real backend's primary key rejects the duplicate, and the
        /// canonical test double must too, or a create path that repeats an identifier passes here and destroys a row
        /// in production. Checked for the whole batch before anything is written.
        /// </summary>
        protected void RequireNewKeys(IReadOnlyCollection<T> items)
        {
            var seen = new HashSet<Guid>();
            foreach (var item in items)
            {
                var key = item.Guid!.Value;
                if (_items.ContainsKey(key) || !seen.Add(key))
                {
                    throw new StoreException($"{GetType().Name} already holds a {typeof(T).Name} with Guid {key}; Create does not replace — use Update.");
                }
            }
        }

        /// <summary>
        /// Adds an entity whose key <see cref="RequireNewKeys"/> cleared; a concurrent create of the same key still
        /// loses rather than overwrites.
        /// </summary>
        protected void AddNew(T item)
        {
            if (!_items.TryAdd(item.Guid!.Value, Snapshot(item)))
            {
                throw new StoreException($"{GetType().Name} already holds a {typeof(T).Name} with Guid {item.Guid}; Create does not replace — use Update.");
            }
        }

        /// <summary>
        /// SH-M308: replaces the stored entity only while one is stored. <c>ContainsKey</c> then the indexer was two
        /// atomic calls, not one, so a delete between them — another thread's, or the store delegate's own — was
        /// undone by the write. A concurrent replacement is retried; a removal ends it.
        /// </summary>
        /// <returns>False when nothing is stored under the key — the caller reports the miss (TASK-555).</returns>
        protected bool ReplaceExisting(T item)
        {
            var key = item.Guid!.Value;
            while (_items.TryGetValue(key, out var current))
            {
                if (_items.TryUpdate(key, Snapshot(item), current))
                {
                    return true;
                }
            }
            return false;
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
