using Birko.Data.Filters;
using Birko.Data.MongoDB.Aggregation;
using Birko.Data.MongoDB.ChangeStreams;
using MongoDB.Bson;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.MongoDB.Stores
{
    /// <summary>
    /// Async MongoDB data store for CRUD and bulk operations.
    /// </summary>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public class AsyncMongoDBStore<T>
        : Data.Stores.AbstractAsyncBulkStore<T>
        , Data.Stores.ISettingsStore<Settings>
        , Data.Stores.IAsyncTransactionalStore<T, IClientSessionHandle>
        where T : Data.Models.AbstractModel
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
        /// Gets the collection for this store.
        /// </summary>
        protected IMongoCollection<T>? Collection
        {
            get
            {
                if (Client != null)
                {
                    return Client.GetCollection<T>();
                }
                return null;
            }
        }

        /// <summary>
        /// Initializes a new instance of the AsyncMongoDBStore class.
        /// </summary>
        public AsyncMongoDBStore()
        {
        }

        /// <summary>
        /// Sets the connection settings.
        /// </summary>
        /// <param name="settings">The MongoDB settings to use.</param>
        public virtual void SetSettings(MongoDB.Stores.Settings settings)
        {
            if (settings != null)
            {
                Client = new MongoDB.MongoDBClient(settings);
            }
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
        public override async Task<T?> ReadAsync(Guid guid, CancellationToken ct = default)
        {
            if (Collection == null)
            {
                return null;
            }

            return await Collection.Find(new ModelByGuid<T>(guid).Filter()).FirstOrDefaultAsync(ct);
        }

        /// <inheritdoc />
        public override async Task<T?> ReadAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default)
        {
            if (Collection == null)
            {
                return null;
            }

            if (filter == null)
            {
                return await Collection.Find(FilterDefinition<T>.Empty).FirstOrDefaultAsync(ct);
            }

            return await Collection.Find(filter).FirstOrDefaultAsync(ct);
        }

        /// <summary>
        /// Reads all entities from MongoDB.
        /// </summary>
        public async Task<IEnumerable<T>> ReadAllAsync(CancellationToken ct = default)
        {
            if (Collection == null)
            {
                return await Task.FromResult(Enumerable.Empty<T>());
            }

            var filter = Builders<T>.Filter.Empty;
            var cursor = await Collection.FindAsync(filter, null, ct);
            return await cursor.ToListAsync(ct);
        }

        /// <inheritdoc />
        public override async Task<long> CountAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default)
        {
            if (Collection == null)
            {
                return 0;
            }

            if (filter == null)
            {
                return await Collection.CountDocumentsAsync(FilterDefinition<T>.Empty, null, ct);
            }

            return await Collection.CountDocumentsAsync(filter, null, ct);
        }

        /// <inheritdoc />
        public override async Task<Guid> CreateAsync(T data, Data.Stores.StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
        {
            if (Collection == null || data == null)
            {
                return Guid.Empty;
            }

            data.Guid ??= Guid.NewGuid();
            processDelegate?.Invoke(data);

            if (TransactionContext != null)
                await Collection.InsertOneAsync(TransactionContext, data, null, ct);
            else
                await Collection.InsertOneAsync(data, null, ct);

            return data.Guid.Value;
        }

        /// <inheritdoc />
        public override async Task UpdateAsync(T data, Data.Stores.StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
        {
            if (Collection == null || data == null || data.Guid == null || data.Guid == Guid.Empty)
            {
                return;
            }

            processDelegate?.Invoke(data);

            var filter = new ModelByGuid<T>(data.Guid.Value).Filter();
            if (TransactionContext != null)
                await Collection.ReplaceOneAsync(TransactionContext, filter, data, new ReplaceOptions { IsUpsert = false }, ct);
            else
                await Collection.ReplaceOneAsync(filter, data, new ReplaceOptions { IsUpsert = false }, ct);
        }

        /// <inheritdoc />
        public override async Task DeleteAsync(T data, CancellationToken ct = default)
        {
            if (Collection == null || data == null || data.Guid == null || data.Guid == Guid.Empty)
            {
                return;
            }

            var filter = new ModelByGuid<T>(data.Guid.Value).Filter();
            if (TransactionContext != null)
                await Collection.DeleteOneAsync(TransactionContext, filter, null, ct);
            else
                await Collection.DeleteOneAsync(filter, ct);
        }

        /// <inheritdoc />
        public override async Task<Guid> SaveAsync(T data, Data.Stores.StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
        {
            if (Collection == null || data == null)
            {
                return await Task.FromResult(Guid.Empty);
            }

            if (data.Guid == null || data.Guid == Guid.Empty)
            {
                await CreateAsync(data, processDelegate, ct);
                return data.Guid ?? Guid.Empty;
            }
            else
            {
                var filter = new ModelByGuid<T>(data.Guid.Value).Filter();
                if (TransactionContext != null)
                    await Collection.ReplaceOneAsync(TransactionContext, filter, data, new ReplaceOptions { IsUpsert = true }, ct);
                else
                    await Collection.ReplaceOneAsync(filter, data, new ReplaceOptions { IsUpsert = true }, ct);
                return data.Guid.Value;
            }
        }

        /// <inheritdoc />
        public override async Task InitAsync(CancellationToken ct = default)
        {
            // MongoDB is schema-less, so no initialization needed
            // Collections are created automatically on first write
            await Task.CompletedTask;
        }

        /// <inheritdoc />
        public override async Task DestroyAsync(CancellationToken ct = default)
        {
            if (Client != null)
            {
                var collectionName = typeof(T).Name;
                await Task.Run(() => Client.DropCollection<T>(collectionName), ct);
            }
        }


        #region Bulk Operations (IAsyncBulkStore<T>)

        /// <inheritdoc />
        public override async Task<IEnumerable<T>> ReadAsync(
            Expression<Func<T, bool>>? filter = null,
            Data.Stores.OrderBy<T>? orderBy = null,
            int? limit = null,
            int? offset = null,
            CancellationToken ct = default)
        {
            if (Collection == null)
            {
                return await Task.FromResult(Enumerable.Empty<T>());
            }

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

            var cursor = await query.ToCursorAsync(ct);
            var results = new List<T>();
            while (await cursor.MoveNextAsync(ct))
            {
                results.AddRange(cursor.Current);
            }
            return results;
        }

        /// <inheritdoc />
        public override async Task CreateAsync(
            IEnumerable<T> data,
            Data.Stores.StoreDataDelegate<T>? storeDelegate = null,
            CancellationToken ct = default)
        {
            if (Collection == null || data == null)
            {
                return;
            }

            var itemsToCreate = data.Where(x => x != null).ToList();
            if (itemsToCreate.Count == 0)
            {
                return;
            }

            foreach (var item in itemsToCreate)
            {
                item.Guid = item.Guid ?? Guid.NewGuid();
                storeDelegate?.Invoke(item);
            }

            if (TransactionContext != null)
                await Collection.InsertManyAsync(TransactionContext, itemsToCreate, null, ct);
            else
                await Collection.InsertManyAsync(itemsToCreate, null, ct);
        }

        /// <inheritdoc />
        public override async Task UpdateAsync(
            IEnumerable<T> data,
            Data.Stores.StoreDataDelegate<T>? storeDelegate = null,
            CancellationToken ct = default)
        {
            if (Collection == null || data == null)
            {
                return;
            }

            var itemsToUpdate = data.Where(x => x != null).ToList();
            if (itemsToUpdate.Count == 0)
            {
                return;
            }

            foreach (var item in itemsToUpdate)
            {
                if (item.Guid == null || item.Guid == Guid.Empty)
                {
                    continue;
                }

                storeDelegate?.Invoke(item);

                var filter = new ModelByGuid<T>(item.Guid.Value).Filter();
                if (TransactionContext != null)
                    await Collection.ReplaceOneAsync(TransactionContext, filter, item, new ReplaceOptions { IsUpsert = false }, ct);
                else
                    await Collection.ReplaceOneAsync(filter, item, new ReplaceOptions { IsUpsert = false }, ct);
            }
        }

        /// <inheritdoc />
        public override async Task DeleteAsync(IEnumerable<T> data, CancellationToken ct = default)
        {
            if (Collection == null || data == null)
            {
                return;
            }

            var guids = data
                .Where(x => x != null && x.Guid != null && x.Guid != Guid.Empty)
                .Select(x => x.Guid!.Value)
                .ToList();

            if (guids.Count == 0)
            {
                return;
            }

            var filter = new ModelsByGuid<T>(guids).Filter();
            if (TransactionContext != null)
                await Collection.DeleteManyAsync(TransactionContext, filter, null, ct);
            else
                await Collection.DeleteManyAsync(filter, ct);
        }

        #endregion

        #region Change Streams

        /// <summary>
        /// Watches the collection for changes and yields change events as they arrive.
        /// Requires a MongoDB replica set or sharded cluster.
        /// </summary>
        /// <param name="options">Optional change stream configuration.</param>
        /// <param name="ct">Cancellation token to stop watching.</param>
        /// <returns>An async enumerable of change stream events.</returns>
        public async IAsyncEnumerable<ChangeStreamEvent<T>> WatchAsync(
            ChangeStreams.ChangeStreamOptions? options = null,
            [EnumeratorCancellation] CancellationToken ct = default)
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

            using var cursor = await Collection.WatchAsync(driverOptions, ct).ConfigureAwait(false);

            while (await cursor.MoveNextAsync(ct).ConfigureAwait(false))
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
