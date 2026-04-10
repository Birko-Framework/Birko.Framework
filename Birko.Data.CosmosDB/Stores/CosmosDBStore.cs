using Birko.Data.Models;
using Birko.Data.Stores;
using Birko.Configuration;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net;

namespace Birko.Data.CosmosDB.Stores;

/// <summary>
/// Azure Cosmos DB implementation of IBulkStore for document-based storage with bulk operations.
/// Uses the Microsoft.Azure.Cosmos SDK v3 with the NoSQL API.
/// </summary>
public class CosmosDBStore<T>
    : AbstractBulkStore<T>
    , ISettingsStore<RemoteSettings>
    where T : AbstractModel
{
    private CosmosClient? _cosmosClient;
    private Database? _database;
    private Container? _container;
    private string? _databaseName;
    private string? _containerName;

    /// <summary>
    /// Gets the underlying Cosmos DB client.
    /// </summary>
    public CosmosClient? Client => _cosmosClient;

    /// <summary>
    /// Gets the underlying Cosmos DB container.
    /// </summary>
    public Container? Container => _container;

    /// <summary>
    /// Partition key path used for the container. Default is "/id".
    /// Set before calling SetSettings or Init to take effect.
    /// </summary>
    public static string PartitionKeyPath { get; set; } = "/id";

    /// <summary>
    /// Request timeout for Cosmos DB operations. Default is 30 seconds.
    /// Set before calling SetSettings to take effect.
    /// </summary>
    public static TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Initializes a new instance of the CosmosDBStore class.
    /// </summary>
    public CosmosDBStore()
    {
    }

    /// <summary>
    /// Initializes a new instance with a connection string.
    /// </summary>
    /// <param name="connectionString">The Cosmos DB connection string.</param>
    /// <param name="databaseName">The database name.</param>
    /// <param name="containerName">The container name. Defaults to the type name.</param>
    public CosmosDBStore(string connectionString, string databaseName, string? containerName = null)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string cannot be empty", nameof(connectionString));
        }
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            throw new ArgumentException("Database name cannot be empty", nameof(databaseName));
        }

        _databaseName = databaseName;
        _containerName = containerName ?? typeof(T).Name;

        _cosmosClient = new CosmosClient(connectionString, new CosmosClientOptions
        {
            RequestTimeout = RequestTimeout,
            AllowBulkExecution = true
        });
    }

    /// <summary>
    /// Initializes a new instance with an existing Cosmos DB container.
    /// </summary>
    /// <param name="container">The Cosmos DB container.</param>
    public CosmosDBStore(Container container)
    {
        _container = container ?? throw new ArgumentNullException(nameof(container));
    }

    #region Settings and Initialization

    /// <summary>
    /// Sets the connection settings.
    /// </summary>
    /// <param name="settings">The remote settings to use.</param>
    public virtual void SetSettings(RemoteSettings settings)
    {
        SetSettings((ISettings)settings);
    }

    /// <summary>
    /// Sets the connection settings via the ISettings interface.
    /// RemoteSettings.Location = connection string or endpoint URL,
    /// RemoteSettings.Name = database name,
    /// RemoteSettings.Password = account key (if using endpoint URL),
    /// RemoteSettings.UserName = container name (optional, defaults to type name).
    /// </summary>
    /// <param name="settings">The settings to use.</param>
    public virtual void SetSettings(ISettings settings)
    {
        if (settings is not RemoteSettings remote)
        {
            return;
        }

        _databaseName = remote.Name;
        _containerName = !string.IsNullOrWhiteSpace(remote.UserName) ? remote.UserName : typeof(T).Name;

        if (!string.IsNullOrWhiteSpace(remote.Password))
        {
            _cosmosClient = new CosmosClient(remote.Location, remote.Password, new CosmosClientOptions
            {
                RequestTimeout = RequestTimeout,
                AllowBulkExecution = true
            });
        }
        else
        {
            _cosmosClient = new CosmosClient(remote.Location, new CosmosClientOptions
            {
                RequestTimeout = RequestTimeout,
                AllowBulkExecution = true
            });
        }
    }

    /// <inheritdoc />
    protected override void InitCore()
    {
        EnsureDatabaseAndContainerExist();
    }

    /// <inheritdoc />
    public override void Destroy()
    {
        if (_container != null)
        {
            _container.DeleteContainerAsync().GetAwaiter().GetResult();
            _container = null;
        }
    }

    #endregion

    #region Core CRUD Operations - Single Item

    /// <inheritdoc />
    protected override Guid CreateCore(T data, StoreDataDelegate<T>? storeDelegate = null)
    {
        if (_container == null || data == null) return Guid.Empty;

        data.Guid ??= Guid.NewGuid();
        storeDelegate?.Invoke(data);

        _container.CreateItemAsync(data, new PartitionKey(data.Guid.Value.ToString()))
            .GetAwaiter().GetResult();

        return data.Guid.Value;
    }

    /// <inheritdoc />
    public override T? Read(Guid guid)
    {
        if (_container == null || guid == Guid.Empty) return null;

        try
        {
            var response = _container.ReadItemAsync<T>(guid.ToString(), new PartitionKey(guid.ToString()))
                .GetAwaiter().GetResult();
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public override IEnumerable<T> Read()
    {
        if (_container == null) return Enumerable.Empty<T>();

        var query = _container.GetItemLinqQueryable<T>(allowSynchronousQueryExecution: true);
        return query.ToList();
    }

    /// <inheritdoc />
    protected override T? ReadCore(Expression<Func<T, bool>>? filter = null)
    {
        if (_container == null) return null;

        var queryable = _container.GetItemLinqQueryable<T>(allowSynchronousQueryExecution: true);

        if (filter != null)
        {
            return queryable.Where(filter).AsEnumerable().FirstOrDefault();
        }

        return queryable.AsEnumerable().FirstOrDefault();
    }

    /// <inheritdoc />
    protected override void UpdateCore(T data, StoreDataDelegate<T>? storeDelegate = null)
    {
        if (_container == null || data == null || data.Guid == null || data.Guid == Guid.Empty) return;

        storeDelegate?.Invoke(data);

        _container.ReplaceItemAsync(data, data.Guid.Value.ToString(), new PartitionKey(data.Guid.Value.ToString()))
            .GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    protected override void DeleteCore(T data)
    {
        if (_container == null || data == null || data.Guid == null || data.Guid == Guid.Empty) return;

        _container.DeleteItemAsync<T>(data.Guid.Value.ToString(), new PartitionKey(data.Guid.Value.ToString()))
            .GetAwaiter().GetResult();
    }

    #endregion

    #region Query and Count Operations

    /// <inheritdoc />
    protected override long CountCore(Expression<Func<T, bool>>? filter = null)
    {
        if (_container == null) return 0;

        var queryable = _container.GetItemLinqQueryable<T>(allowSynchronousQueryExecution: true);

        if (filter != null)
        {
            return queryable.Count(filter);
        }

        return queryable.Count();
    }

    #endregion

    #region Core CRUD Operations - Bulk

    /// <inheritdoc />
    protected override IEnumerable<T> ReadCore(Expression<Func<T, bool>>? filter = null, OrderBy<T>? orderBy = null, int? limit = null, int? offset = null)
    {
        if (_container == null) return Enumerable.Empty<T>();

        IQueryable<T> query = _container.GetItemLinqQueryable<T>(allowSynchronousQueryExecution: true);

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

        return query.ToList();
    }

    /// <inheritdoc />
    protected override void CreateCore(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null)
    {
        if (_container == null || data == null) return;

        var tasks = new List<System.Threading.Tasks.Task>();

        foreach (var item in data)
        {
            if (item == null) continue;

            item.Guid ??= Guid.NewGuid();
            storeDelegate?.Invoke(item);

            tasks.Add(_container.CreateItemAsync(item, new PartitionKey(item.Guid.Value.ToString())));
        }

        System.Threading.Tasks.Task.WhenAll(tasks).GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    protected override void UpdateCore(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null)
    {
        if (_container == null || data == null) return;

        var tasks = new List<System.Threading.Tasks.Task>();

        foreach (var item in data)
        {
            if (item == null || item.Guid == null || item.Guid == Guid.Empty) continue;

            storeDelegate?.Invoke(item);
            tasks.Add(_container.ReplaceItemAsync(item, item.Guid.Value.ToString(), new PartitionKey(item.Guid.Value.ToString())));
        }

        System.Threading.Tasks.Task.WhenAll(tasks).GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    protected override void DeleteCore(IEnumerable<T> data)
    {
        if (_container == null || data == null) return;

        var tasks = new List<System.Threading.Tasks.Task>();

        foreach (var item in data)
        {
            if (item == null || item.Guid == null || item.Guid == Guid.Empty) continue;

            tasks.Add(_container.DeleteItemAsync<T>(item.Guid.Value.ToString(), new PartitionKey(item.Guid.Value.ToString())));
        }

        System.Threading.Tasks.Task.WhenAll(tasks).GetAwaiter().GetResult();
    }

    #endregion

    #region Database Utilities

    /// <summary>
    /// Ensures the database and container exist, creating them if needed.
    /// </summary>
    public void EnsureDatabaseAndContainerExist()
    {
        if (_cosmosClient == null || string.IsNullOrEmpty(_databaseName)) return;

        _database = _cosmosClient.CreateDatabaseIfNotExistsAsync(_databaseName)
            .GetAwaiter().GetResult().Database;

        _container = _database.CreateContainerIfNotExistsAsync(
            _containerName ?? typeof(T).Name,
            PartitionKeyPath
        ).GetAwaiter().GetResult().Container;
    }

    /// <summary>
    /// Checks if the Cosmos DB endpoint is reachable.
    /// </summary>
    /// <returns>True if the endpoint is reachable, false otherwise.</returns>
    public bool IsHealthy()
    {
        if (_cosmosClient == null) return false;

        try
        {
            _cosmosClient.ReadAccountAsync().GetAwaiter().GetResult();
            return true;
        }
        catch
        {
            return false;
        }
    }

    #endregion
}
