using Birko.Data.CosmosDB.Aggregation;
using Birko.Data.Models;
using Birko.Data.Stores;
using ISettings = Birko.Configuration.ISettings;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using System;
using Birko.Data.Exceptions;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.CosmosDB.Stores;

/// <summary>
/// Async Azure Cosmos DB implementation of IAsyncBulkStore with transactional batch support.
/// Uses the Microsoft.Azure.Cosmos SDK v3 with the NoSQL API.
/// </summary>
public class AsyncCosmosDBStore<T>
    : AbstractAsyncBulkStore<T>
    , ISettingsStore<Settings>, ISettingsStore<Birko.Configuration.ISettings>
    , IAsyncTransactionalStore<T, TransactionalBatch>
    , IAsyncAggregatableStore<T>
    where T : AbstractModel
{
    private CosmosClient? _cosmosClient;
    private Database? _database;
    private Container? _container;
    private string? _databaseName;
    private string? _containerName;
    protected Settings? _settings;

    /// <summary>
    /// Gets the underlying Cosmos DB client.
    /// </summary>
    public CosmosClient? Client => _cosmosClient;

    /// <summary>
    /// Gets the underlying Cosmos DB container.
    /// </summary>
    public Container? Container => _container;

    /// <inheritdoc />
    public TransactionalBatch? TransactionContext { get; private set; }

    // The partition key every item added to the current batch has had so far. A TransactionalBatch is
    // scoped to ONE logical partition; this store derives each item's partition key from its Guid, so the
    // first item added fixes the batch's partition and anything else cannot legally join it.
    private string? _batchPartitionKey;

    /// <inheritdoc />
    public void SetTransactionContext(TransactionalBatch? context)
    {
        TransactionContext = context;
        _batchPartitionKey = null;
    }

    /// <summary>
    /// Refuses an item whose partition key differs from the one the current batch is already pinned to.
    /// </summary>
    /// <remarks>
    /// TASK-240. Cosmos transactional batches are limited to a single logical partition key. This store
    /// partitions by <c>Guid</c>, so <b>every document is its own logical partition</b> and a boundary
    /// spanning two entities is impossible by construction — the second <c>CreateItem</c> was accepted
    /// silently and the whole batch then failed at <c>ExecuteAsync</c> with an opaque BadRequest, or, if
    /// the caller never inspected the response, was simply lost.
    /// <para>
    /// Refusing at the call site names the limit while the caller's stack still points at the write that
    /// broke it. It is deliberately checked here rather than trusted to the server, because "the API let
    /// me type it" is exactly how a boundary silently stops covering what a caller thinks it covers.
    /// </para>
    /// </remarks>
    private void RequireBatchPartition(Guid guid)
    {
        var key = guid.ToString();
        if (_batchPartitionKey == null)
        {
            _batchPartitionKey = key;
            return;
        }
        if (!string.Equals(_batchPartitionKey, key, StringComparison.Ordinal))
        {
            throw new CosmosTransactionScopeException(_batchPartitionKey, key);
        }
    }

    /// <summary>
    /// Initializes a new instance of the AsyncCosmosDBStore class.
    /// </summary>
    public AsyncCosmosDBStore()
    {
    }

    /// <summary>
    /// Initializes a new instance with a connection string.
    /// </summary>
    /// <param name="connectionString">The Cosmos DB connection string.</param>
    /// <param name="databaseName">The database name.</param>
    /// <param name="containerName">The container name. Defaults to the type name.</param>
    /// <param name="settings">
    /// Optional settings supplying the client options — chiefly <c>ConnectionMode</c>, which cannot be
    /// expressed in a Cosmos connection string and defaults to Direct. Pass a Settings with
    /// <c>ConnectionMode = Gateway</c> to reach an account through a proxy/firewall, or the emulator
    /// (TASK-223). Omit for the previous behaviour.
    /// </param>
    public AsyncCosmosDBStore(string connectionString, string databaseName, string? containerName = null, Settings? settings = null)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string cannot be empty", nameof(connectionString));
        }
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            throw new ArgumentException("Database name cannot be empty", nameof(databaseName));
        }

        _settings = settings ?? new Settings();
        _settings.RequireAddressablePartitionKeyPath();
        _databaseName = databaseName;
        _containerName = containerName ?? typeof(T).Name;

        _cosmosClient = new CosmosClient(connectionString, _settings.GetCosmosClientOptions());
    }

    /// <summary>
    /// Initializes a new instance with an existing Cosmos DB container.
    /// </summary>
    /// <param name="container">The Cosmos DB container.</param>
    public AsyncCosmosDBStore(Container container)
    {
        _container = container ?? throw new ArgumentNullException(nameof(container));
    }

    #region Settings and Initialization

    /// <summary>
    /// Sets the connection settings.
    /// </summary>
    /// <param name="settings">The CosmosDB settings to use.</param>
    public virtual void SetSettings(Settings settings)
    {
        SetSettings((ISettings)settings);
    }

    /// <summary>
    /// Sets the connection settings via the ISettings interface.
    /// Settings.Location = connection string or endpoint URL,
    /// Settings.Name = database name,
    /// Settings.Password = account key (if using endpoint URL),
    /// Settings.UserName = container name (optional, defaults to type name).
    /// </summary>
    /// <param name="settings">The settings to use.</param>
    public virtual void SetSettings(ISettings settings)
    {
        if (settings is Settings cosmosSettings)
        {
            _settings = cosmosSettings;
        }
        else if (settings is Birko.Configuration.RemoteSettings remote)
        {
            _settings = new Settings();
            _settings.LoadFrom(remote);
        }
        else
        {
            return;
        }

        _settings.RequireAddressablePartitionKeyPath();
        _databaseName = _settings.Name;
        _containerName = !string.IsNullOrWhiteSpace(_settings.UserName) ? _settings.UserName : typeof(T).Name;

        if (!string.IsNullOrWhiteSpace(_settings.Password))
        {
            _cosmosClient = new CosmosClient(_settings.Location, _settings.Password, _settings.GetCosmosClientOptions());
        }
        else
        {
            _cosmosClient = new CosmosClient(_settings.Location, _settings.GetCosmosClientOptions());
        }
    }

    /// <inheritdoc />
    protected override async Task InitCoreAsync(CancellationToken ct = default)
    {
        // TASK-812: every public operation passes through Init first, so a store that was never configured refuses here (reads too: an empty answer from a store that cannot look is a false one), as the SQL stores do (TASK-170).
        if (_container == null && _cosmosClient == null)
            throw new InvalidOperationException(
                $"{GetType().Name} has no Cosmos DB client: call SetSettings with the store's settings first.");
        await EnsureDatabaseAndContainerExistAsync(ct);
    }

    /// <inheritdoc />
    protected override async Task DestroyCoreAsync(CancellationToken ct = default)
    {
        if (_container != null)
        {
            await _container.DeleteContainerAsync(cancellationToken: ct);
            _container = null;
        }
    }

    #endregion

    #region Core CRUD Operations - Single Item

    /// <inheritdoc />

    /// <summary>
    /// TASK-625: a create refused because the id is taken is a 409 Conflict, single or from the bulk create's parallel
    /// writes (which may arrive wrapped in an AggregateException). The other items of a bulk create may already be stored.
    /// </summary>
    protected override bool IsDuplicateKeyException(Exception ex)
    {
        if (ex is AggregateException agg) return agg.Flatten().InnerExceptions.Any(IsDuplicateKeyException);
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e is CosmosException c && c.StatusCode == HttpStatusCode.Conflict) return true;
        }
        return false;
    }

    protected override async Task<Guid> CreateCoreAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
    {
        if (_container == null || data == null) return Guid.Empty;

        data.EnsureGuid();
        data = StoreDelegates.Apply(processDelegate, data);

        if (TransactionContext != null)
        {
            RequireBatchPartition(data.Guid.Value);
            TransactionContext.CreateItem(data);
            return data.Guid.Value;
        }

        await _container.CreateItemAsync(data, new PartitionKey(data.Guid.Value.ToString()), cancellationToken: ct);
        return data.Guid.Value;
    }

    /// <inheritdoc />
    public override async Task<T?> ReadAsync(Guid guid, CancellationToken ct = default)
    {
        // This overrides the public wrapper to keep Cosmos's efficient point-read, so it must run
        // the lazy-init gate itself (CR-H042: a connectionString-constructed store read before an
        // explicit InitAsync had _container == null and returned null instead of auto-initializing).
        await EnsureInitializedAsync(ct).ConfigureAwait(false);
        if (_container == null || guid == Guid.Empty) return null;

        try
        {
            var response = await _container.ReadItemAsync<T>(guid.ToString(), new PartitionKey(guid.ToString()), cancellationToken: ct);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <inheritdoc />
    protected override async Task<T?> ReadCoreAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default)
    {
        filter = CosmosFilterRewrite.Prepare(filter);   // TASK-220, 224, 867, 896: one pre-pass, shared with the view store
        if (_container == null) return null;

        // TASK-834: with no order, "the first match" is any match, so the chunks are asked in turn.
        var chunks = PlanInList(filter, "read", sortedOrPaged: false);
        if (chunks == null) return await ReadFirstOneAsync(filter, ct);
        foreach (var chunk in chunks)
        {
            var found = await ReadFirstOneAsync(chunk, ct);
            if (found != null) return found;
        }
        return null;
    }

    private async Task<T?> ReadFirstOneAsync(Expression<Func<T, bool>>? filter, CancellationToken ct)
    {
        var queryable = _container!.GetItemLinqQueryable<T>();

        if (filter != null)
        {
            queryable = (IOrderedQueryable<T>)queryable.Where(filter);
        }

        using var iterator = queryable.ToFeedIterator();
        if (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(ct);
            return response.FirstOrDefault();
        }

        return null;
    }

    #region IN-list chunking (TASK-834)

    /// <summary>
    /// Values per chunk when a filter's <c>list.Contains(x.Col)</c> is split (TASK-834). The measured ceiling is
    /// 13,105 Guids per query (Cosmos DB's 524,288-character query text); 1000 keeps every chunk far under it.
    /// </summary>
    protected virtual int InListChunkSize => CosmosInListChunking.DefaultChunkSize;

    /// <summary>The query-text length, in characters, past which Cosmos DB refuses a query (SC3020).</summary>
    protected virtual int MaxQueryTextLength => CosmosInListChunking.DefaultMaxQueryTextLength;

    /// <summary>
    /// The chunk filters for <paramref name="filter"/>, or <c>null</c> to run it as one query. Refuses with
    /// <see cref="NotSupportedException"/>, before any request, a list past the ceiling that cannot be split.
    /// </summary>
    private IReadOnlyList<Expression<Func<T, bool>>>? PlanInList(
        Expression<Func<T, bool>>? filter, string operation, bool sortedOrPaged,
        OrderBy<T>? orderBy = null, int? limit = null, int? offset = null)
        => CosmosInListChunking.Plan(filter, operation, InListChunkSize, MaxQueryTextLength, sortedOrPaged,
            f => BuildQuery(f, orderBy, limit, offset).ToQueryDefinition().QueryText.Length);

    private async Task<IReadOnlyList<Expression<Func<T, bool>>>?> PlanInListForWriteAsync(
        Expression<Func<T, bool>> filter, string operation, CancellationToken ct)
    {
        await EnsureInitializedAsync(ct).ConfigureAwait(false);
        if (_container == null) return null;
        filter = CosmosFilterRewrite.Prepare(filter)!;   // TASK-220, 224, 867, 896: one pre-pass, shared with the view store
        return PlanInList(filter, operation, sortedOrPaged: false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// TASK-834: an IN list longer than <see cref="InListChunkSize"/> is updated chunk by chunk. A failure after the
    /// first chunk is a <see cref="CosmosChunkedWriteException"/> naming the chunks applied; nothing is rolled back.
    /// </remarks>
    public override async Task UpdateAsync(Expression<Func<T, bool>> filter, Action<T> updateAction, CancellationToken ct = default)
    {
        RequireFilter(filter, "update");
        RequireBoundedFilter(filter, "update");
        var chunks = await PlanInListForWriteAsync(filter, "update", ct).ConfigureAwait(false);
        if (chunks == null)
        {
            await base.UpdateAsync(filter, updateAction, ct).ConfigureAwait(false);
            return;
        }
        await CosmosInListChunking.RunWritesAsync(chunks, "update", chunk => base.UpdateAsync(chunk, updateAction, ct)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>TASK-834: chunked as <see cref="UpdateAsync(Expression{Func{T, bool}}, Action{T}, CancellationToken)"/> is.</remarks>
    public override async Task DeleteAsync(Expression<Func<T, bool>> filter, CancellationToken ct = default)
    {
        RequireFilter(filter, "delete");
        RequireBoundedFilter(filter, "delete");
        var chunks = await PlanInListForWriteAsync(filter, "delete", ct).ConfigureAwait(false);
        if (chunks == null)
        {
            await base.DeleteAsync(filter, ct).ConfigureAwait(false);
            return;
        }
        await CosmosInListChunking.RunWritesAsync(chunks, "delete", chunk => base.DeleteAsync(chunk, ct)).ConfigureAwait(false);
    }

    #endregion

    /// <inheritdoc />
    protected override async Task UpdateCoreAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
    {
        if (_container == null || data == null) return;
        if (data.Guid == null || data.Guid == Guid.Empty)
        {
            throw new EntityNotFoundException(typeof(T), data.Guid);
        }

        data = StoreDelegates.Apply(processDelegate, data);

        if (TransactionContext != null)
        {
            // A replace inside a transactional batch is checked when the batch executes: a missing item fails the
            // whole batch atomically at commit, which reports it (TASK-555 changes nothing here).
            RequireBatchPartition(data.Guid.Value);
            TransactionContext.ReplaceItem(data.Guid.Value.ToString(), data);
            return;
        }

        try
        {
            await _container.ReplaceItemAsync(data, data.Guid.Value.ToString(), new PartitionKey(data.Guid.Value.ToString()), cancellationToken: ct);
        }
        catch (CosmosException ex) when (IsItemNotFound(ex))
        {
            throw new EntityNotFoundException(typeof(T), data.Guid);
        }
    }

    /// <summary>
    /// SH-M306 (TASK-555): a replace of an item that is not stored answers 404. Sub-status 1003 is the CONTAINER
    /// missing — an infrastructure failure, not a miss — so it keeps propagating as the CosmosException it is.
    /// </summary>
    private static bool IsItemNotFound(CosmosException ex)
        => ex.StatusCode == HttpStatusCode.NotFound && ex.SubStatusCode != 1003;

    /// <inheritdoc />
    protected override async Task DeleteCoreAsync(T data, CancellationToken ct = default)
    {
        if (_container == null || data == null || data.Guid == null || data.Guid == Guid.Empty) return;

        if (TransactionContext != null)
        {
            RequireBatchPartition(data.Guid.Value);
            TransactionContext.DeleteItem(data.Guid.Value.ToString());
            return;
        }

        await DeleteIfStoredAsync(data.Guid.Value, ct);
    }

    /// <summary>
    /// TASK-719: deleting a key that is not stored is a no-op, as on every other provider; the raw 404
    /// <see cref="CosmosException"/> reached the caller (measured). A missing container (1003) still propagates.
    /// </summary>
    private async Task DeleteIfStoredAsync(Guid guid, CancellationToken ct)
    {
        try
        {
            await _container!.DeleteItemAsync<T>(guid.ToString(), new PartitionKey(guid.ToString()), cancellationToken: ct).ConfigureAwait(false);
        }
        catch (CosmosException ex) when (IsItemNotFound(ex))
        {
        }
    }

    /// <summary>
    /// Conflicts <see cref="UpdateIfAsync"/> absorbs before it reports <see cref="StoreConcurrencyException"/>. Each one
    /// means another writer changed the item, so the condition is evaluated again on what it wrote.
    /// </summary>
    public const int MaxConditionalUpdateAttempts = 16;

    /// <summary>
    /// A compare-and-set on one item (TASK-661): reads it with its ETag, applies <paramref name="update"/> only when
    /// <paramref name="condition"/> holds for the stored item, and replaces it only if nobody wrote it in between
    /// (<c>IfMatchEtag</c>). On a lost race the item is read again and the condition asked again, so a caller sees either
    /// its change applied to the current item or <c>null</c>, never a write over a newer version.
    /// </summary>
    /// <returns>The item as written, or <c>null</c> when it is missing or the condition does not hold.</returns>
    /// <exception cref="StoreConcurrencyException">The item kept changing for <see cref="MaxConditionalUpdateAttempts"/> reads.</exception>
    /// <exception cref="InvalidOperationException">Called inside a transactional batch, which cannot read and decide.</exception>
    public virtual async Task<T?> UpdateIfAsync(Guid guid, Func<T, bool> condition, Action<T> update, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(update);
        await EnsureInitializedAsync(ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        if (TransactionContext != null)
        {
            throw new InvalidOperationException(
                "UpdateIfAsync reads the item and decides before it writes; a transactional batch defers every write to its commit, so it cannot run inside one.");
        }
        if (_container == null || guid == Guid.Empty) return null;

        var id = guid.ToString();
        var partition = new PartitionKey(id);
        for (var attempt = 1; attempt <= MaxConditionalUpdateAttempts; attempt++)
        {
            ItemResponse<T> read;
            try
            {
                read = await _container.ReadItemAsync<T>(id, partition, cancellationToken: ct).ConfigureAwait(false);
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            var item = read.Resource;
            if (item == null || !condition(item)) return null;
            update(item);

            try
            {
                await _container.ReplaceItemAsync(item, id, partition, new ItemRequestOptions { IfMatchEtag = read.ETag }, ct).ConfigureAwait(false);
                return item;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed)
            {
                // Another writer got there first: read what it wrote and ask the condition again.
            }
            catch (CosmosException ex) when (IsItemNotFound(ex))
            {
                return null;
            }
        }

        throw new StoreConcurrencyException(
            $"Item {guid} of {typeof(T).Name} changed under {MaxConditionalUpdateAttempts} consecutive conditional updates.",
            applied: 0, conflicts: MaxConditionalUpdateAttempts);
    }

    #endregion

    #region Query and Count Operations

    /// <inheritdoc />
    protected override async Task<long> CountCoreAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default)
    {
        filter = CosmosFilterRewrite.Prepare(filter);   // TASK-220, 224, 867, 896: one pre-pass, shared with the view store
        if (_container == null) return 0;

        // TASK-834: the chunks' values are distinct, so their counts are disjoint and add up.
        var chunks = PlanInList(filter, "count", sortedOrPaged: false);
        if (chunks == null) return await CountOneAsync(filter, ct);
        long total = 0;
        foreach (var chunk in chunks)
        {
            total += await CountOneAsync(chunk, ct);
        }
        return total;
    }

    private async Task<long> CountOneAsync(Expression<Func<T, bool>>? filter, CancellationToken ct)
    {
        var queryable = _container!.GetItemLinqQueryable<T>();

        if (filter != null)
        {
            queryable = (IOrderedQueryable<T>)queryable.Where(filter);
        }

        return await queryable.CountAsync(ct);
    }

    #endregion

    #region Utility Methods

    /// <inheritdoc />
    public override async Task<Guid> SaveAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
    {
        // TASK-555: this overrides the PUBLIC wrapper, so it must run the lazy-init + cancellation gate itself (the slip
        // CR-H077 fixed in the RavenDB store). Without it a SaveAsync as the first call on a store found no container,
        // returned Guid.Empty and wrote nothing, silently. Found by the update-miss contract, measured on the emulator.
        await EnsureInitializedAsync(ct);
        if (_container == null || data == null) return Guid.Empty;

        // TASK-719: the Guid first, as CreateAsync does, so the delegate sees the entity's identity; it ran on a Guid-less
        // entity (measured).
        data.EnsureGuid();
        data = StoreDelegates.Apply(processDelegate, data);

        if (TransactionContext != null)
        {
            RequireBatchPartition(data.Guid!.Value);
            TransactionContext.UpsertItem(data);
            return data.Guid.Value;
        }

        try
        {
            await _container.UpsertItemAsync(data, new PartitionKey(data.Guid!.Value.ToString()), cancellationToken: ct);
        }
        catch (Exception ex) when (ex is not StoreUniqueViolationException && IsDuplicateKeyException(ex))
        {
            // TASK-719: the native upsert bypasses the create path that names a collision (a unique-key policy's 409).
            throw StoreException.UniqueViolation(typeof(T), ex);
        }
        return data.Guid.Value;
    }

    #endregion

    #region Core CRUD Operations - Bulk

    /// <inheritdoc />
    protected override async Task<IEnumerable<T>> ReadCoreAsync(
        Expression<Func<T, bool>>? filter = null,
        OrderBy<T>? orderBy = null,
        int? limit = null,
        int? offset = null,
        CancellationToken ct = default)
    {
        if (_container == null) return Enumerable.Empty<T>();
        filter = CosmosFilterRewrite.Prepare(filter);   // TASK-220, 224, 867, 896: one pre-pass, shared with the view store

        // TASK-834: a long IN list is read chunk by chunk, the results concatenated (the chunks are disjoint). A sort,
        // limit or offset cannot be reproduced over chunks, so such a read stays one query, refused past the ceiling.
        var sortedOrPaged = orderBy?.Fields.Count > 0 || limit.HasValue || offset.HasValue;
        var dateTimeOrder = CosmosDateTimeOrder.Plan(orderBy, typeof(T).Name);   // TASK-902
        var chunks = PlanInList(filter, "read", sortedOrPaged, orderBy, limit, offset);
        if (chunks == null && dateTimeOrder != null)
        {
            return await CosmosDateTimeOrder.ReadAsync(filter, dateTimeOrder, limit, offset,
                async (f, l, o) => await ReadAllAsync(BuildQuery(f, orderBy, l, o), ct).ConfigureAwait(false)).ConfigureAwait(false);
        }
        if (chunks == null) return await ReadAllAsync(BuildQuery(filter, orderBy, limit, offset), ct);

        var results = new List<T>();
        foreach (var chunk in chunks)
        {
            results.AddRange(await ReadAllAsync(BuildQuery(chunk, null, null, null), ct));
        }
        return results;
    }

    private static async Task<List<T>> ReadAllAsync(IQueryable<T> query, CancellationToken ct)
    {
        var results = new List<T>();
        using var iterator = query.ToFeedIterator();
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(ct);
            results.AddRange(response);
        }

        return results;
    }

    private IQueryable<T> BuildQuery(Expression<Func<T, bool>>? filter, OrderBy<T>? orderBy, int? limit, int? offset)
    {
        IQueryable<T> query = _container!.GetItemLinqQueryable<T>();

        if (filter != null)
        {
            query = query.Where(filter);
        }

        if (orderBy?.Fields.Count > 0)
        {
            for (int i = 0; i < orderBy.Fields.Count; i++)
            {
                var field = orderBy.Fields[i];
                var param = Expression.Parameter(typeof(T), "x");
                var property = Expression.Property(param, field.PropertyName);
                var lambda = Expression.Lambda(property, param);

                var methodName = i == 0
                    ? (field.Descending ? "OrderByDescending" : "OrderBy")
                    : (field.Descending ? "ThenByDescending" : "ThenBy");

                var method = typeof(Queryable).GetMethods()
                    .First(m => m.Name == methodName && m.GetParameters().Length == 2)
                    .MakeGenericMethod(typeof(T), property.Type);

                query = (IQueryable<T>)method.Invoke(null, new object[] { query, lambda })!;
            }
        }

        if (offset.HasValue)
        {
            query = query.Skip(offset.Value);
        }

        if (limit.HasValue)
        {
            query = query.Take(limit.Value);
        }

        return query;
    }

    /// <inheritdoc />
    protected override async Task CreateCoreAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default)
    {
        if (_container == null || data == null) return;

        if (TransactionContext != null)
        {
            foreach (var itemSource in data)
            {
                var item = itemSource;
                if (item == null) continue;
                item.EnsureGuid();
                item = StoreDelegates.Apply(storeDelegate, item);
                RequireBatchPartition(item.Guid!.Value);
                TransactionContext.CreateItem(item);
            }
            return;
        }

        var tasks = new List<Task>();
        foreach (var itemSource in data)
        {
            var item = itemSource;
            if (item == null) continue;

            item.EnsureGuid();
            item = StoreDelegates.Apply(storeDelegate, item);

            tasks.Add(_container.CreateItemAsync(item, new PartitionKey(item.Guid.Value.ToString()), cancellationToken: ct));
        }

        await Task.WhenAll(tasks);
    }

    /// <inheritdoc />
    protected override async Task UpdateCoreAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default)
    {
        if (_container == null || data == null) return;

        if (TransactionContext != null)
        {
            // TASK-719: an entity with no Guid is reported, as on the non-batch path; the batch skipped it silently.
            var unidentified = 0;
            foreach (var itemSource in data)
            {
                var item = itemSource;
                if (item == null) continue;
                if (item.Guid == null || item.Guid == Guid.Empty)
                {
                    unidentified++;
                    continue;
                }
                item = StoreDelegates.Apply(storeDelegate, item);
                RequireBatchPartition(item.Guid!.Value);
                TransactionContext.ReplaceItem(item.Guid.Value.ToString(), item);
            }
            if (unidentified > 0)
            {
                throw new EntityNotFoundException(typeof(T), Enumerable.Repeat(Guid.Empty, unidentified));
            }
            return;
        }

        // TASK-555: each replace reports its own miss — Task.WhenAll surfaced only the first failure — and the stored
        // items are still written. Any other failure propagates as before.
        var missing = new System.Collections.Concurrent.ConcurrentBag<Guid>();
        var tasks = new List<Task>();
        foreach (var itemSource in data)
        {
            var item = itemSource;
            if (item == null) continue;
            if (item.Guid == null || item.Guid == Guid.Empty)
            {
                missing.Add(Guid.Empty);
                continue;
            }

            item = StoreDelegates.Apply(storeDelegate, item);
            tasks.Add(ReplaceOrRecordMissAsync(item, missing, ct));
        }

        await Task.WhenAll(tasks);
        EntityNotFoundException.ThrowIfAny(typeof(T), missing);
    }

    private async Task ReplaceOrRecordMissAsync(T item, System.Collections.Concurrent.ConcurrentBag<Guid> missing, CancellationToken ct)
    {
        try
        {
            await _container!.ReplaceItemAsync(item, item.Guid!.Value.ToString(), new PartitionKey(item.Guid.Value.ToString()), cancellationToken: ct).ConfigureAwait(false);
        }
        catch (CosmosException ex) when (IsItemNotFound(ex))
        {
            missing.Add(item.Guid!.Value);
        }
    }

    /// <inheritdoc />
    protected override async Task DeleteCoreAsync(IEnumerable<T> data, CancellationToken ct = default)
    {
        if (_container == null || data == null) return;

        if (TransactionContext != null)
        {
            foreach (var item in data)
            {
                if (item == null || item.Guid == null || item.Guid == Guid.Empty) continue;
                RequireBatchPartition(item.Guid.Value);
                TransactionContext.DeleteItem(item.Guid.Value.ToString());
            }
            return;
        }

        var tasks = new List<Task>();
        foreach (var item in data)
        {
            if (item == null || item.Guid == null || item.Guid == Guid.Empty) continue;

            tasks.Add(DeleteIfStoredAsync(item.Guid.Value, ct));   // TASK-719
        }

        await Task.WhenAll(tasks);
    }

    #endregion

    #region Database Utilities

    /// <summary>
    /// Ensures the database and container exist, creating them if needed.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task EnsureDatabaseAndContainerExistAsync(CancellationToken ct = default)
    {
        if (_cosmosClient == null || string.IsNullOrEmpty(_databaseName)) return;

        var dbResponse = await _cosmosClient.CreateDatabaseIfNotExistsAsync(_databaseName, cancellationToken: ct);
        _database = dbResponse.Database;

        var containerResponse = await _database.CreateContainerIfNotExistsAsync(
            _containerName ?? typeof(T).Name,
            _settings?.EffectivePartitionKeyPath ?? "/id",
            cancellationToken: ct
        );
        _container = containerResponse.Container;
    }

    /// <summary>
    /// Checks if the Cosmos DB endpoint is reachable.
    /// </summary>
    /// <returns>True if the endpoint is reachable, false otherwise.</returns>
    public async Task<bool> IsHealthyAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested(); // CR-L103: observe the token (ReadAccountAsync has no ct overload)
        if (_cosmosClient == null) return false;

        try
        {
            await _cosmosClient.ReadAccountAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks if the Cosmos DB endpoint is reachable (sync wrapper).
    /// </summary>
    public bool IsHealthy()
    {
        return IsHealthyAsync().GetAwaiter().GetResult();
    }

    #endregion

    #region Aggregation

    /// <summary>
    /// Executes an aggregation query using native Cosmos DB SQL aggregation.
    /// Builds a GROUP BY query with aggregate functions (SUM, AVG, MIN, MAX, COUNT).
    /// </summary>
    /// <remarks>
    /// TASK-876: <c>query.Filter</c> is rendered as the store's own reads render it (<see cref="BuildAggregateQuery"/>);
    /// it used to be dropped. The store initialises first, as every read does: before, an aggregate on a fresh store
    /// answered nothing.
    /// </remarks>
    public async Task<IReadOnlyList<AggregateResult>> AggregateAsync(
        AggregateQuery<T> query,
        CancellationToken ct = default)
    {
        if (query == null) throw new ArgumentNullException(nameof(query));
        query.ThrowIfStringOrderNotOrdinal($"Cosmos DB aggregate of {typeof(T).Name}");   // TASK-907: before any data
        await EnsureInitializedAsync(ct).ConfigureAwait(false);
        if (_container == null) return Array.Empty<AggregateResult>();

        var plan = BuildAggregateQuery(query);

        var results = new List<AggregateResult>();
        using var iterator = _container.GetItemQueryIterator<System.Text.Json.JsonElement>(plan.Query);
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(ct);
            foreach (var doc in response)
            {
                results.Add(plan.Read(doc));
            }
        }

        results = AggregateHelper.ApplyOrderingAndPaging(results, query.OrderBy, query.Offset, query.Limit);

        return results.AsReadOnly();
    }

    /// <summary>
    /// TASK-876: the aggregation query, its WHERE rendered exactly as a read renders the filter: the same rewrites, then
    /// <see cref="BuildQuery"/>'s <c>Where</c>. Groups cannot be merged across IN-list chunks, so a list past the ceiling
    /// is refused by name before any request, as a sorted read's is. TASK-885: the plan also reads each answered row
    /// (nullable SUM, time bucket), and refuses a time bucket it cannot answer before any request.
    /// </summary>
    private CosmosAggregatePlan BuildAggregateQuery(AggregateQuery<T> query)
    {
        var filter = CosmosFilterRewrite.Prepare(query.Filter);   // TASK-220, 224, 867, 896: one pre-pass, shared with the view store

        CosmosAggregatePlan Render(Expression<Func<T, bool>>? f)
            => CosmosAggregationHelper.BuildAggregateQuery(query, f == null ? null : BuildQuery(f, null, null, null).ToQueryDefinition());

        CosmosInListChunking.Plan(filter, "aggregate", InListChunkSize, MaxQueryTextLength, sortedOrPaged: true,
            f => Render(f).Query.QueryText.Length, "groups and aggregates cannot be combined across chunks");
        return Render(filter);
    }

    #endregion
}
