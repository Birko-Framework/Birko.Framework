using Birko.Data.CosmosDB.Aggregation;
using Birko.Data.Models;
using Birko.Data.Stores;
using ISettings = Birko.Configuration.ISettings;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using System;
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
    , ISettingsStore<Settings>
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

    /// <inheritdoc />
    public void SetTransactionContext(TransactionalBatch? context)
    {
        TransactionContext = context;
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
    public AsyncCosmosDBStore(string connectionString, string databaseName, string? containerName = null)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string cannot be empty", nameof(connectionString));
        }
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            throw new ArgumentException("Database name cannot be empty", nameof(databaseName));
        }

        _settings = new Settings();
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
        await EnsureDatabaseAndContainerExistAsync(ct);
    }

    /// <inheritdoc />
    public override async Task DestroyAsync(CancellationToken ct = default)
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
    protected override async Task<Guid> CreateCoreAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
    {
        if (_container == null || data == null) return Guid.Empty;

        data.Guid ??= Guid.NewGuid();
        processDelegate?.Invoke(data);

        if (TransactionContext != null)
        {
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
        if (_container == null) return null;

        var queryable = _container.GetItemLinqQueryable<T>();

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

    /// <inheritdoc />
    protected override async Task UpdateCoreAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
    {
        if (_container == null || data == null || data.Guid == null || data.Guid == Guid.Empty) return;

        processDelegate?.Invoke(data);

        if (TransactionContext != null)
        {
            TransactionContext.ReplaceItem(data.Guid.Value.ToString(), data);
            return;
        }

        await _container.ReplaceItemAsync(data, data.Guid.Value.ToString(), new PartitionKey(data.Guid.Value.ToString()), cancellationToken: ct);
    }

    /// <inheritdoc />
    protected override async Task DeleteCoreAsync(T data, CancellationToken ct = default)
    {
        if (_container == null || data == null || data.Guid == null || data.Guid == Guid.Empty) return;

        if (TransactionContext != null)
        {
            TransactionContext.DeleteItem(data.Guid.Value.ToString());
            return;
        }

        await _container.DeleteItemAsync<T>(data.Guid.Value.ToString(), new PartitionKey(data.Guid.Value.ToString()), cancellationToken: ct);
    }

    #endregion

    #region Query and Count Operations

    /// <inheritdoc />
    protected override async Task<long> CountCoreAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default)
    {
        if (_container == null) return 0;

        var queryable = _container.GetItemLinqQueryable<T>();

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
        if (_container == null || data == null) return Guid.Empty;

        processDelegate?.Invoke(data);

        if (data.Guid == null || data.Guid == Guid.Empty)
        {
            data.Guid = Guid.NewGuid();
        }

        if (TransactionContext != null)
        {
            TransactionContext.UpsertItem(data);
            return data.Guid.Value;
        }

        await _container.UpsertItemAsync(data, new PartitionKey(data.Guid.Value.ToString()), cancellationToken: ct);
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

        IQueryable<T> query = _container.GetItemLinqQueryable<T>();

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

        var results = new List<T>();
        using var iterator = query.ToFeedIterator();
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(ct);
            results.AddRange(response);
        }

        return results;
    }

    /// <inheritdoc />
    protected override async Task CreateCoreAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default)
    {
        if (_container == null || data == null) return;

        if (TransactionContext != null)
        {
            foreach (var item in data)
            {
                if (item == null) continue;
                item.Guid ??= Guid.NewGuid();
                storeDelegate?.Invoke(item);
                TransactionContext.CreateItem(item);
            }
            return;
        }

        var tasks = new List<Task>();
        foreach (var item in data)
        {
            if (item == null) continue;

            item.Guid ??= Guid.NewGuid();
            storeDelegate?.Invoke(item);

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
            foreach (var item in data)
            {
                if (item == null || item.Guid == null || item.Guid == Guid.Empty) continue;
                storeDelegate?.Invoke(item);
                TransactionContext.ReplaceItem(item.Guid.Value.ToString(), item);
            }
            return;
        }

        var tasks = new List<Task>();
        foreach (var item in data)
        {
            if (item == null || item.Guid == null || item.Guid == Guid.Empty) continue;

            storeDelegate?.Invoke(item);
            tasks.Add(_container.ReplaceItemAsync(item, item.Guid.Value.ToString(), new PartitionKey(item.Guid.Value.ToString()), cancellationToken: ct));
        }

        await Task.WhenAll(tasks);
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
                TransactionContext.DeleteItem(item.Guid.Value.ToString());
            }
            return;
        }

        var tasks = new List<Task>();
        foreach (var item in data)
        {
            if (item == null || item.Guid == null || item.Guid == Guid.Empty) continue;

            tasks.Add(_container.DeleteItemAsync<T>(item.Guid.Value.ToString(), new PartitionKey(item.Guid.Value.ToString()), cancellationToken: ct));
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
            _settings?.PartitionKeyPath ?? "/id",
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
    public async Task<IReadOnlyList<AggregateResult>> AggregateAsync(
        AggregateQuery<T> query,
        CancellationToken ct = default)
    {
        if (_container == null) return Array.Empty<AggregateResult>();

        var sql = CosmosAggregationHelper.BuildAggregateSql(query);
        var queryDef = new QueryDefinition(sql);

        var results = new List<AggregateResult>();
        using var iterator = _container.GetItemQueryIterator<System.Text.Json.JsonElement>(queryDef);
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(ct);
            foreach (var doc in response)
            {
                results.Add(new AggregateResult(CosmosAggregationHelper.ParseJsonResult(doc)));
            }
        }

        results = AggregateHelper.ApplyOrderingAndPaging(results, query.OrderBy, query.Offset, query.Limit);

        return results.AsReadOnly();
    }

    #endregion
}
