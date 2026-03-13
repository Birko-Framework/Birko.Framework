using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json;

using Birko.Data.Stores;

namespace Birko.Data.JSON.Stores
{
    /// <summary>
    /// Abstract base class for synchronous JSON file-based data stores with bulk operations.
    /// </summary>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public abstract class AbstractJsonStore<T> : AbstractBulkStore<T>
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
        /// Initializes a new instance of the AbstractJsonStore class.
        /// </summary>
        public AbstractJsonStore()
        {
        }

        #endregion

        #region Core CRUD Operations - Single Item

        /// <inheritdoc />
        public override T? Read(Expression<Func<T, bool>>? filter = null)
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
        public override Guid Create(T data, StoreDataDelegate<T>? storeDelegate = null)
        {
            data.Guid ??= Guid.NewGuid();
            storeDelegate?.Invoke(data);
            _items.Add(data.Guid.Value, data);
            SaveData();
            return data.Guid.Value;
        }

        /// <inheritdoc />
        public override void Update(T data, StoreDataDelegate<T>? storeDelegate = null)
        {
            if (data.Guid != null && (_items?.ContainsKey(data.Guid.Value) ?? false))
            {
                storeDelegate?.Invoke(data);
                _items[data.Guid.Value] = data;
                SaveData();
            }
        }

        /// <inheritdoc />
        public override void Delete(T data)
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
        public override long Count(Expression<Func<T, bool>>? filter = null)
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
        /// Deserializes data from a stream.
        /// </summary>
        /// <typeparam name="TData">Type of data to deserialize.</typeparam>
        /// <param name="stream">The stream to read from.</param>
        /// <returns>The deserialized data.</returns>
        protected static TData? ReadFromStream<TData>(FileStream stream)
        {
            return JsonSerializer.Deserialize<TData>(stream);
        }

        /// <summary>
        /// Serializes data to a stream.
        /// </summary>
        /// <typeparam name="TData">Type of data to serialize.</typeparam>
        /// <param name="stream">The stream to write to.</param>
        /// <param name="data">The data to serialize.</param>
        protected static void WriteToStream<TData>(FileStream stream, TData data)
        {
            using Utf8JsonWriter jsonWriter = new(stream, new JsonWriterOptions()
            {
                Indented = true,
            });
            JsonSerializer.Serialize(jsonWriter, data);
        }

        #endregion

        #region Core CRUD Operations - Bulk

        /// <inheritdoc />
        public override IEnumerable<T> Read(Expression<Func<T, bool>>? filter = null, OrderBy<T>? orderBy = null, int? limit = null, int? offset = null)
        {
            var result = _items?.Values?.Where(x => filter?.Compile()?.Invoke(x) ?? true);

            if (orderBy != null && orderBy.Fields.Count > 0)
            {
                result = ApplyOrderBy(result!, orderBy);
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
        public override void Create(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null)
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
        public override void Update(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null)
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
        public override void Delete(IEnumerable<T> data)
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
    }
}
