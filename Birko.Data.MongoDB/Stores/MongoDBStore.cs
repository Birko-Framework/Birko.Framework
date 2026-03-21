using Birko.Data.Filters;
using Birko.Data.MongoDB.Aggregation;
using Birko.Data.MongoDB.ChangeStreams;
using MongoDB.Bson;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Birko.Data.MongoDB.Stores
{
    /// <summary>
    /// Synchronous MongoDB data store for CRUD and bulk operations.
    /// </summary>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public class MongoDBStore<T>
        : Data.Stores.AbstractBulkStore<T>
        , Data.Stores.ISettingsStore<Settings>
        , Data.Stores.ITransactionalStore<T, IClientSessionHandle>
        where T : Models.MongoDBModel
    {
        /// <summary>
        /// Gets the MongoDB client.
        /// </summary>
        public MongoDB.MongoDBClient? Client { get; private set; }

        /// <inheritdoc />
        public IClientSessionHandle? TransactionContext { get; private set; }

        /// <inheritdoc />
        public void SetTransactionContext(IClientSessionHandle? context)
        {
            TransactionContext = context;
        }

        /// <summary>
        /// The settings for this store.
        /// </summary>
        protected MongoDB.Stores.Settings? _settings = null;

        /// <summary>
        /// Gets the collection for this store.
        /// </summary>
        protected IMongoCollection<T>? Collection
        {
            get
            {
                if (Client == null) return null;
                return Client.GetCollection<T>();
            }
        }

        /// <summary>
        /// Initializes a new instance of the MongoDBStore class.
        /// </summary>
        public MongoDBStore()
        {
        }

        /// <summary>
        /// Sets the connection settings.
        /// </summary>
        /// <param name="settings">The MongoDB settings to use.</param>
        public virtual void SetSettings(MongoDB.Stores.Settings settings)
        {
            _settings = settings;
            Client = new MongoDB.MongoDBClient(settings);
        }

        /// <summary>
        /// Sets the connection settings via the ISettings interface.
        /// </summary>
        /// <param name="settings">The settings to use.</param>
        public virtual void SetSettings(Birko.Configuration.ISettings settings)
        {
            if (settings is MongoDB.Stores.Settings mongoSettings)
            {
                SetSettings(mongoSettings);
            }
        }

        /// <inheritdoc />
        public override void Init()
        {
            // MongoDB is schema-less, so no initialization needed
            // Collections are created automatically on first write
        }

        /// <inheritdoc />
        public override void Destroy()
        {
            if (Client == null) return;
            var collectionName = typeof(T).Name;
            Client.DropCollection<T>(collectionName);
        }

        /// <inheritdoc />
        public override T? Read(Guid guid)
        {
            if (Collection == null) return null;

            return Collection.Find(new ModelByGuid<T>(guid).Filter()).FirstOrDefault();
        }

        /// <inheritdoc />
        public override IEnumerable<T> Read()
        {
            if (Collection == null) return Enumerable.Empty<T>();

            return Collection.Find(FilterDefinition<T>.Empty).ToList();
        }

        /// <inheritdoc />
        public override T? Read(Expression<Func<T, bool>>? filter = null)
        {
            if (Collection == null) return null;

            if (filter == null)
            {
                return Collection.Find(FilterDefinition<T>.Empty).FirstOrDefault();
            }

            return Collection.Find(filter).FirstOrDefault();
        }

        /// <inheritdoc />
        public override long Count(Expression<Func<T, bool>>? filter = null)
        {
            if (Collection == null) return 0;

            if (filter == null)
            {
                return Collection.CountDocuments(FilterDefinition<T>.Empty);
            }

            return Collection.CountDocuments(filter);
        }

        /// <inheritdoc />
        public override Guid Create(T data, Data.Stores.StoreDataDelegate<T>? storeDelegate = null)
        {
            if (Collection == null || data == null) return Guid.Empty;

            data.Guid ??= Guid.NewGuid();
            storeDelegate?.Invoke(data);

            if (TransactionContext != null)
                Collection.InsertOne(TransactionContext, data);
            else
                Collection.InsertOne(data);

            return data.Guid.Value;
        }

        /// <inheritdoc />
        public override void Update(T data, Data.Stores.StoreDataDelegate<T>? storeDelegate = null)
        {
            if (Collection == null || data == null || data.Guid == null || data.Guid == Guid.Empty) return;

            storeDelegate?.Invoke(data);

            var filter = new ModelByGuid<T>(data.Guid.Value).Filter();
            if (TransactionContext != null)
                Collection.ReplaceOne(TransactionContext, filter, data, new ReplaceOptions { IsUpsert = false });
            else
                Collection.ReplaceOne(filter, data, new ReplaceOptions { IsUpsert = false });
        }

        /// <inheritdoc />
        public override void Delete(T data)
        {
            if (Collection == null || data == null || data.Guid == null || data.Guid == Guid.Empty) return;

            var filter = new ModelByGuid<T>(data.Guid.Value).Filter();
            if (TransactionContext != null)
                Collection.DeleteOne(TransactionContext, filter);
            else
                Collection.DeleteOne(filter);
        }

        #region Bulk Operations (IBulkStore<T>)

        /// <inheritdoc />
        public override IEnumerable<T> Read(Expression<Func<T, bool>>? filter = null, Data.Stores.OrderBy<T>? orderBy = null, int? limit = null, int? offset = null)
        {
            if (Collection == null) return Enumerable.Empty<T>();

            var query = Collection.Find(filter ?? FilterDefinition<T>.Empty);

            if (orderBy?.Fields.Count > 0)
            {
                var sortBuilder = Builders<T>.Sort;
                var sorts = orderBy.Fields.Select(f => f.Descending
                    ? sortBuilder.Descending(f.PropertyName)
                    : sortBuilder.Ascending(f.PropertyName));
                query = query.Sort(sortBuilder.Combine(sorts));
            }

            if (offset.HasValue)
            {
                query = query.Skip(offset.Value);
            }

            if (limit.HasValue)
            {
                query = query.Limit(limit.Value);
            }

            return query.ToList();
        }

        /// <inheritdoc />
        public override void Create(IEnumerable<T> data, Data.Stores.StoreDataDelegate<T>? storeDelegate = null)
        {
            if (Collection == null || data == null) return;

            var itemsToCreate = data.Where(x => x != null).ToList();
            if (itemsToCreate.Count == 0) return;

            foreach (var item in itemsToCreate)
            {
                item.Guid = item.Guid ?? Guid.NewGuid();
                storeDelegate?.Invoke(item);
            }

            if (TransactionContext != null)
                Collection.InsertMany(TransactionContext, itemsToCreate);
            else
                Collection.InsertMany(itemsToCreate);
        }

        /// <inheritdoc />
        public override void Update(IEnumerable<T> data, Data.Stores.StoreDataDelegate<T>? storeDelegate = null)
        {
            if (Collection == null || data == null) return;

            var itemsToUpdate = data.Where(x => x != null).ToList();
            if (itemsToUpdate.Count == 0) return;

            foreach (var item in itemsToUpdate)
            {
                if (item.Guid == null || item.Guid == Guid.Empty)
                {
                    continue;
                }

                storeDelegate?.Invoke(item);

                var filter = new ModelByGuid<T>(item.Guid.Value).Filter();
                if (TransactionContext != null)
                    Collection.ReplaceOne(TransactionContext, filter, item, new ReplaceOptions { IsUpsert = false });
                else
                    Collection.ReplaceOne(filter, item, new ReplaceOptions { IsUpsert = false });
            }
        }

        /// <inheritdoc />
        public override void Delete(IEnumerable<T> data)
        {
            if (Collection == null || data == null) return;

            var guids = data
                .Where(x => x != null && x.Guid != null && x.Guid != Guid.Empty)
                .Select(x => x.Guid!.Value)
                .ToList();

            if (guids.Count == 0) return;

            var filter = new ModelsByGuid<T>(guids).Filter();
            if (TransactionContext != null)
                Collection.DeleteMany(TransactionContext, filter);
            else
                Collection.DeleteMany(filter);
        }

        #endregion

        #region Change Streams

        /// <summary>
        /// Watches the collection for changes and yields change events as they arrive.
        /// This is a blocking operation. Requires a MongoDB replica set or sharded cluster.
        /// </summary>
        /// <param name="options">Optional change stream configuration.</param>
        /// <returns>An enumerable of change stream events.</returns>
        public IEnumerable<ChangeStreamEvent<T>> Watch(ChangeStreams.ChangeStreamOptions? options = null)
        {
            if (Collection == null)
            {
                yield break;
            }

            var driverOptions = new global::MongoDB.Driver.ChangeStreamOptions();
            if (options != null)
            {
                driverOptions.FullDocument = options.FullDocument;

                if (options.BatchSize.HasValue)
                {
                    driverOptions.BatchSize = options.BatchSize.Value;
                }

                if (options.MaxAwaitTime.HasValue)
                {
                    driverOptions.MaxAwaitTime = options.MaxAwaitTime.Value;
                }

                if (options.ResumeAfter != null)
                {
                    driverOptions.ResumeAfter = options.ResumeAfter;
                }

                if (options.StartAfter != null)
                {
                    driverOptions.StartAfter = options.StartAfter;
                }
            }

            using var cursor = Collection.Watch(driverOptions);

            while (cursor.MoveNext())
            {
                foreach (var change in cursor.Current)
                {
                    yield return MapChangeStreamDocument(change);
                }
            }
        }

        private static ChangeStreamEvent<T> MapChangeStreamDocument(ChangeStreamDocument<T> change)
        {
            var evt = new ChangeStreamEvent<T>
            {
                FullDocument = change.FullDocument,
                ClusterTime = change.ClusterTime,
                ResumeToken = change.ResumeToken
            };

            evt.OperationType = change.OperationType switch
            {
                global::MongoDB.Driver.ChangeStreamOperationType.Insert => ChangeStreams.ChangeStreamOperationType.Insert,
                global::MongoDB.Driver.ChangeStreamOperationType.Update => ChangeStreams.ChangeStreamOperationType.Update,
                global::MongoDB.Driver.ChangeStreamOperationType.Replace => ChangeStreams.ChangeStreamOperationType.Replace,
                global::MongoDB.Driver.ChangeStreamOperationType.Delete => ChangeStreams.ChangeStreamOperationType.Delete,
                global::MongoDB.Driver.ChangeStreamOperationType.Invalidate => ChangeStreams.ChangeStreamOperationType.Invalidate,
                global::MongoDB.Driver.ChangeStreamOperationType.Drop => ChangeStreams.ChangeStreamOperationType.Drop,
                _ => ChangeStreams.ChangeStreamOperationType.Invalidate
            };

            if (change.DocumentKey != null && change.DocumentKey.Contains("_id"))
            {
                var idValue = change.DocumentKey["_id"];
                if (idValue.IsGuid)
                {
                    evt.DocumentKey = idValue.AsGuid;
                }
            }

            return evt;
        }

        #endregion

        #region Aggregation

        /// <summary>
        /// Creates a new aggregation pipeline builder bound to this store's collection.
        /// </summary>
        /// <returns>A new <see cref="AggregationPipelineBuilder{T}"/> instance.</returns>
        public AggregationPipelineBuilder<T> Aggregate()
        {
            if (Collection == null)
            {
                throw new InvalidOperationException("Cannot create aggregation pipeline: store is not configured. Call SetSettings first.");
            }

            return new AggregationPipelineBuilder<T>(Collection);
        }

        #endregion
    }
}
