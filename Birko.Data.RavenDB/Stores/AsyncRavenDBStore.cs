using Birko.Data.Models;
using Birko.Data.Stores;
using Raven.Client.Documents;
using Raven.Client.Documents.Indexes;
using Raven.Client.Documents.Linq;
using Raven.Client.Documents.Operations.Indexes;
using Raven.Client.ServerWide;
using Raven.Client.ServerWide.Operations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.RavenDB.Stores;

/// <summary>
/// Async RavenDB implementation of IAsyncBulkStore with bulk operations.
/// </summary>
public class AsyncRavenDBStore<T>
    : AbstractAsyncBulkStore<T>
    , ISettingsStore<RemoteSettings>
    , IAsyncTransactionalStore<T, Raven.Client.Documents.Session.IAsyncDocumentSession>
    where T : AbstractModel
{
    private IDocumentStore? _documentStore;

    /// <summary>
    /// Get the underlying RavenDB document store.
    /// </summary>
    public IDocumentStore? DocumentStore => _documentStore;

    /// <inheritdoc />
    public Raven.Client.Documents.Session.IAsyncDocumentSession? TransactionContext { get; private set; }

    /// <inheritdoc />
    public void SetTransactionContext(Raven.Client.Documents.Session.IAsyncDocumentSession? context)
    {
        TransactionContext = context;
    }

    /// <summary>
    /// Initializes a new instance of the AsyncRavenDBStore class.
    /// </summary>
    public AsyncRavenDBStore()
    {
    }

    /// <summary>
    /// Initializes a new instance with a connection string.
    /// </summary>
    /// <param name="connectionString">The RavenDB server URL.</param>
    /// <param name="databaseName">The database name.</param>
    public AsyncRavenDBStore(string connectionString, string? databaseName = null)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string cannot be empty", nameof(connectionString));
        }

        _documentStore = new DocumentStore
        {
            Urls = new[] { connectionString },
            Database = databaseName
        };

        _documentStore.Initialize();
    }

    /// <summary>
    /// Initializes a new instance with an existing document store.
    /// </summary>
    /// <param name="documentStore">The RavenDB document store.</param>
    public AsyncRavenDBStore(IDocumentStore documentStore)
    {
        _documentStore = documentStore ?? throw new ArgumentNullException(nameof(documentStore));
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
    /// </summary>
    /// <param name="settings">The settings to use.</param>
    public virtual void SetSettings(ISettings settings)
    {
        if (settings is RemoteSettings remote)
        {
            _documentStore = new DocumentStore
            {
                Urls = new[] { remote.Location },
                Database = remote.Name
            };
            _documentStore.Initialize();
        }
    }

    /// <inheritdoc />
    public override async Task InitAsync(CancellationToken ct = default)
    {
        EnsureDatabaseExists();
        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public override async Task DestroyAsync(CancellationToken ct = default)
    {
        var dbName = _documentStore?.Database;
        if (!string.IsNullOrEmpty(dbName))
        {
            await _documentStore!.Maintenance.Server.SendAsync(
                new DeleteDatabasesOperation(dbName, hardDelete: true),
                ct
            );
        }
    }

    #endregion

    #region Core CRUD Operations - Single Item

    /// <inheritdoc />
    public override async Task<Guid> CreateAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
    {
        if (_documentStore == null || data == null)
        {
            return Guid.Empty;
        }

        data.Guid ??= Guid.NewGuid();
        processDelegate?.Invoke(data);

        if (TransactionContext != null)
        {
            await TransactionContext.StoreAsync(data);
            return data.Guid.Value;
        }

        using var session = _documentStore.OpenAsyncSession();
        await session.StoreAsync(data);
        await session.SaveChangesAsync(ct);

        return data.Guid.Value;
    }

    /// <inheritdoc />
    public override async Task<T?> ReadAsync(Guid guid, CancellationToken ct = default)
    {
        if (_documentStore == null || guid == Guid.Empty)
        {
            return null;
        }

        if (TransactionContext != null)
        {
            return await TransactionContext.LoadAsync<T>(guid.ToString(), ct);
        }

        using var session = _documentStore.OpenAsyncSession();
        return await session.LoadAsync<T>(guid.ToString(), ct);
    }

    /// <inheritdoc />
    public override async Task<T?> ReadAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default)
    {
        if (_documentStore == null)
        {
            return null;
        }

        if (TransactionContext != null)
        {
            if (filter != null)
            {
                return await TransactionContext.Query<T>().Where(filter).FirstOrDefaultAsync(ct);
            }
            return await TransactionContext.Query<T>().FirstOrDefaultAsync(ct);
        }

        using var session = _documentStore.OpenAsyncSession();

        if (filter != null)
        {
            return await session.Query<T>().Where(filter).FirstOrDefaultAsync(ct);
        }

        return await session.Query<T>().FirstOrDefaultAsync(ct);
    }

    /// <inheritdoc />
    public override async Task UpdateAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
    {
        if (_documentStore == null || data == null || data.Guid == null || data.Guid == Guid.Empty)
        {
            return;
        }

        processDelegate?.Invoke(data);

        if (TransactionContext != null)
        {
            var existing = await TransactionContext.LoadAsync<T>(data.Guid.Value.ToString(), ct);
            if (existing != null)
            {
                TransactionContext.Advanced.Evict(existing);
            }
            await TransactionContext.StoreAsync(data);
            return;
        }

        using var session = _documentStore.OpenAsyncSession();
        var existingItem = await session.LoadAsync<T>(data.Guid.Value.ToString(), ct);

        if (existingItem != null)
        {
            session.Advanced.Evict(existingItem);
        }

        await session.StoreAsync(data);
        await session.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public override async Task DeleteAsync(T data, CancellationToken ct = default)
    {
        if (_documentStore == null || data == null || data.Guid == null || data.Guid == Guid.Empty)
        {
            return;
        }

        if (TransactionContext != null)
        {
            TransactionContext.Delete(data.Guid.Value.ToString());
            return;
        }

        using var session = _documentStore.OpenAsyncSession();
        session.Delete(data.Guid.Value.ToString());
        await session.SaveChangesAsync(ct);
    }

    #endregion

    #region Query and Count Operations

    /// <inheritdoc />
    public override async Task<long> CountAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default)
    {
        if (_documentStore == null)
        {
            return 0;
        }

        if (TransactionContext != null)
        {
            if (filter != null)
            {
                return await TransactionContext.Query<T>().CountAsync(filter, ct);
            }
            return await TransactionContext.Query<T>().CountAsync(ct);
        }

        using var session = _documentStore.OpenAsyncSession();

        if (filter != null)
        {
            return await session.Query<T>().CountAsync(filter, ct);
        }

        return await session.Query<T>().CountAsync(ct);
    }

    #endregion

    #region Utility Methods

    /// <inheritdoc />
    public override async Task<Guid> SaveAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
    {
        if (_documentStore == null || data == null)
        {
            return Guid.Empty;
        }

        processDelegate?.Invoke(data);

        if (data.Guid == null || data.Guid == Guid.Empty)
        {
            data.Guid = Guid.NewGuid();
        }

        if (TransactionContext != null)
        {
            await TransactionContext.StoreAsync(data);
            return data.Guid.Value;
        }

        using var session = _documentStore.OpenAsyncSession();
        await session.StoreAsync(data);
        await session.SaveChangesAsync(ct);

        return data.Guid.Value;
    }

    #endregion

    #region Core CRUD Operations - Bulk

    /// <inheritdoc />
    public override async Task<IEnumerable<T>> ReadAsync(
        Expression<Func<T, bool>>? filter = null,
        OrderBy<T>? orderBy = null,
        int? limit = null,
        int? offset = null,
        CancellationToken ct = default)
    {
        if (_documentStore == null)
        {
            return Enumerable.Empty<T>();
        }

        var session = TransactionContext ?? _documentStore.OpenAsyncSession();
        try
        {
            IRavenQueryable<T> query = session.Query<T>();

            if (filter != null)
            {
                query = query.Where(filter);
            }

            if (offset.HasValue)
            {
                query = (IRavenQueryable<T>)query.Skip(offset.Value);
            }

            if (limit.HasValue)
            {
                query = (IRavenQueryable<T>)query.Take(limit.Value);
            }

            var results = await query.ToListAsync(ct);

            if (orderBy?.Fields.Count > 0)
            {
                IOrderedEnumerable<T>? ordered = null;
                foreach (var field in orderBy.Fields)
                {
                    var prop = typeof(T).GetProperty(field.PropertyName);
                    if (prop == null) continue;
                    ordered = ordered == null
                        ? (field.Descending ? results.OrderByDescending(x => prop.GetValue(x)) : results.OrderBy(x => prop.GetValue(x)))
                        : (field.Descending ? ordered.ThenByDescending(x => prop.GetValue(x)) : ordered.ThenBy(x => prop.GetValue(x)));
                }
                return ordered?.ToList() ?? results;
            }

            return results;
        }
        finally
        {
            if (TransactionContext == null)
            {
                session.Dispose();
            }
        }
    }

    /// <inheritdoc />
    public override async Task CreateAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default)
    {
        if (_documentStore == null || data == null)
        {
            return;
        }

        if (TransactionContext != null)
        {
            foreach (var item in data)
            {
                if (item == null) continue;
                item.Guid = Guid.NewGuid();
                storeDelegate?.Invoke(item);
                await TransactionContext.StoreAsync(item);
            }
            return;
        }

        using var bulkInsert = _documentStore.BulkInsert();

        foreach (var item in data)
        {
            if (item == null) continue;

            item.Guid = Guid.NewGuid();
            storeDelegate?.Invoke(item);

            await bulkInsert.StoreAsync(item);
        }
    }

    /// <inheritdoc />
    public override async Task UpdateAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default)
    {
        if (_documentStore == null || data == null)
        {
            return;
        }

        var session = TransactionContext ?? _documentStore.OpenAsyncSession();
        try
        {
            foreach (var item in data)
            {
                if (item == null || item.Guid == null || item.Guid == Guid.Empty)
                {
                    continue;
                }

                storeDelegate?.Invoke(item);

                var existing = await session.LoadAsync<T>(item.Guid.Value.ToString(), ct);
                if (existing != null)
                {
                    session.Advanced.Evict(existing);
                }

                await session.StoreAsync(item);
            }

            if (TransactionContext == null)
            {
                await session.SaveChangesAsync(ct);
            }
        }
        finally
        {
            if (TransactionContext == null)
            {
                session.Dispose();
            }
        }
    }

    /// <inheritdoc />
    public override async Task DeleteAsync(IEnumerable<T> data, CancellationToken ct = default)
    {
        if (_documentStore == null || data == null)
        {
            return;
        }

        var session = TransactionContext ?? _documentStore.OpenAsyncSession();
        try
        {
            foreach (var item in data)
            {
                if (item == null || item.Guid == null || item.Guid == Guid.Empty)
                {
                    continue;
                }

                session.Delete(item.Guid.Value.ToString());
            }

            if (TransactionContext == null)
            {
                await session.SaveChangesAsync(ct);
            }
        }
        finally
        {
            if (TransactionContext == null)
            {
                session.Dispose();
            }
        }
    }

    #endregion

    #region Index Operations

    /// <summary>
    /// Creates an index on the RavenDB collection for this store.
    /// </summary>
    /// <param name="indexName">The name of the index.</param>
    /// <param name="indexDefinition">The index definition function.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task CreateIndexAsync(
        string indexName,
        System.Action<IndexDefinitionBuilder<T>> indexDefinition,
        CancellationToken ct = default)
    {
        if (_documentStore == null)
        {
            return;
        }

        var builder = new IndexDefinitionBuilder<T>();
        indexDefinition(builder);
        var indexDefinitionFinal = builder.ToIndexDefinition(conventions: _documentStore.Conventions);
        indexDefinitionFinal.Name = indexName;

        await _documentStore.Maintenance.ForDatabase(_documentStore.Database)
            .SendAsync(new PutIndexesOperation(indexDefinitionFinal), ct);
    }

    /// <summary>
    /// Drops a specific index by name.
    /// </summary>
    /// <param name="indexName">The name of the index to drop.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task DropIndexAsync(string indexName, CancellationToken ct = default)
    {
        if (_documentStore == null)
        {
            return;
        }

        await _documentStore.Maintenance.ForDatabase(_documentStore.Database)
            .SendAsync(new DeleteIndexOperation(indexName), ct);
    }

    #endregion

    #region Database Utilities

    /// <summary>
    /// Check if the database exists.
    /// </summary>
    public bool DatabaseExists()
    {
        var dbName = _documentStore?.Database;
        if (string.IsNullOrEmpty(dbName))
        {
            return true;
        }

        var databaseRecord = _documentStore!.Maintenance.Server.SendAsync(
            new GetDatabaseRecordOperation(dbName)
        ).GetAwaiter().GetResult();

        return databaseRecord != null;
    }

    /// <summary>
    /// Create the database if it doesn't exist.
    /// </summary>
    public void EnsureDatabaseExists()
    {
        var dbName = _documentStore?.Database;
        if (!string.IsNullOrEmpty(dbName) && !DatabaseExists())
        {
            _documentStore!.Maintenance.Server.SendAsync(
                new CreateDatabaseOperation(new DatabaseRecord(dbName))
            ).GetAwaiter().GetResult();
        }
    }

    /// <summary>
    /// Checks if the RavenDB server is reachable.
    /// </summary>
    /// <returns>True if the server is reachable, false otherwise.</returns>
    public bool IsHealthy()
    {
        if (_documentStore == null)
        {
            return false;
        }

        try
        {
            using var session = _documentStore.OpenSession();
            session.Query<T>().Take(0).ToList();
            return true;
        }
        catch
        {
            return false;
        }
    }

    #endregion
}
