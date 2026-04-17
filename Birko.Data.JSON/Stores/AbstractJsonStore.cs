using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;

using Birko.Data.Stores;
using Birko.Configuration;
using Birko.Serialization;
using Birko.Serialization.Json;

namespace Birko.Data.JSON.Stores
{
    /// <summary>
    /// Abstract base class for synchronous JSON file-based data stores with bulk operations.
    /// </summary>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public abstract class AbstractJsonStore<T> : AbstractBulkStore<T>, IAggregatableStore<T>
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
        /// Initializes a new instance of the AbstractJsonStore class with a JSON serializer.
        /// </summary>
        /// <param name="serializer">The JSON serializer to use. If null, creates a default SystemJsonSerializer.</param>
        protected AbstractJsonStore(ISerializer? serializer = null)
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
        protected override T? ReadCore(Expression<Func<T, bool>>? filter = null)
        {
            return _items?.Values.Where(x => filter?.Compile()?.Invoke(x) ?? true)?.FirstOrDefault() ?? null;
        }

        /// <inheritdoc />
        public override T? Read(Guid guid)
        {
            if (_items != null && guid != Guid.Empty && _items.ContainsKey(guid))
            {
                return _items[guid];
            }
            return null;
        }

        /// <inheritdoc />
        protected override Guid CreateCore(T data, StoreDataDelegate<T>? storeDelegate = null)
        {
            data.Guid ??= Guid.NewGuid();
            storeDelegate?.Invoke(data);
            _items.Add(data.Guid.Value, data);
            SaveData();
            return data.Guid.Value;
        }

        /// <inheritdoc />
        protected override void UpdateCore(T data, StoreDataDelegate<T>? storeDelegate = null)
        {
            if (data.Guid != null && (_items?.ContainsKey(data.Guid.Value) ?? false))
            {
                storeDelegate?.Invoke(data);
                _items[data.Guid.Value] = data;
                SaveData();
            }
        }

        /// <inheritdoc />
        protected override void DeleteCore(T data)
        {
            if (data.Guid != null && (_items?.ContainsKey(data.Guid.Value) ?? false))
            {
                _items.Remove(data.Guid.Value);
                SaveData();
            }
        }

        #endregion

        #region Query and Count Operations

        /// <inheritdoc />
        protected override long CountCore(Expression<Func<T, bool>>? filter = null)
        {
            return _items?.Where(x => filter?.Compile()?.Invoke(x.Value) ?? true)?.Count() ?? 0;
        }

        #endregion

        #region Data Persistence

        /// <summary>
        /// Loads data from the JSON file.
        /// </summary>
        protected abstract void LoadData();

        /// <summary>
        /// Saves data to the JSON file.
        /// </summary>
        protected abstract void SaveData();

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

        /// <inheritdoc />
        protected override IEnumerable<T> ReadCore(Expression<Func<T, bool>>? filter = null, OrderBy<T>? orderBy = null, int? limit = null, int? offset = null)
        {
            var result = _items?.Values?.Where(x => filter?.Compile()?.Invoke(x) ?? true);

            if (orderBy != null && orderBy.Fields.Count > 0)
            {
                result = OrderByHelper.ApplyTo(result!, orderBy);
            }

            if (offset != null)
            {
                result = result?.Skip(offset.Value);
            }
            if (limit != null)
            {
                result = result?.Take(limit.Value);
            }
            return result ?? Enumerable.Empty<T>();
        }

        /// <inheritdoc />
        protected override void CreateCore(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null)
        {
            bool save = false;
            foreach (var item in data.Where(x => x != null))
            {
                item.Guid = Guid.NewGuid();
                storeDelegate?.Invoke(item);
                _items.Add(item.Guid.Value, item);
                save = true;
            }
            if (save)
            {
                SaveData();
            }
        }

        /// <inheritdoc />
        protected override void UpdateCore(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null)
        {
            bool save = false;
            foreach (var item in data.Where(x => x != null))
            {
                if (item.Guid != null && (_items?.ContainsKey(item.Guid.Value) ?? false))
                {
                    storeDelegate?.Invoke(item);
                    _items[item.Guid.Value] = item;
                    save = true;
                }
            }
            if (save)
            {
                SaveData();
            }
        }

        /// <inheritdoc />
        protected override void DeleteCore(IEnumerable<T> data)
        {
            bool save = false;
            foreach (var item in data.Where(x => x != null))
            {
                if (item.Guid != null && (_items?.ContainsKey(item.Guid.Value) ?? false))
                {
                    _items.Remove(item.Guid.Value);
                    save = true;
                }
            }
            if (save)
            {
                SaveData();
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
