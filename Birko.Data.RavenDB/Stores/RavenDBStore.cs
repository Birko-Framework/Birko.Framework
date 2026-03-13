using Birko.Data.Models;
using Birko.Data.Stores;
using Raven.Client.Documents;
using Raven.Client.Documents.Linq;
using Raven.Client.Documents.Operations.Indexes;
using Raven.Client.ServerWide;
using Raven.Client.ServerWide.Operations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Birko.Data.Stores;

/// <summary>
/// RavenDB implementation of IBulkStore for document-based storage with bulk operations.
/// </summary>
public class RavenDBStore<T>
    : AbstractBulkStore<T>
    , ISettingsStore<RemoteSettings>
    , ITransactionalStore<T, Raven.Client.Documents.Session.IDocumentSession>
    where T : AbstractModel
{
    private IDocumentStore? _documentStore;

    /// <summary>
    /// Get the underlying RavenDB document store.
    /// </summary>
    public IDocumentStore? DocumentStore => _documentStore;

    /// <inheritdoc />
    public Raven.Client.Documents.Session.IDocumentSession? TransactionContext { get; private set; }

    /// <inheritdoc />
    public void SetTransactionContext(Raven.Client.Documents.Session.IDocumentSession? context)
    {
        TransactionContext = context;
    }

    /// <summary>
    /// Initializes a new instance of the RavenDBStore class.
    /// </summary>
    public RavenDBStore()
    {
    }

    /// <summary>
    /// Initializes a new instance with a connection string.
    /// </summary>
    /// <param name="connectionString">The RavenDB server URL.</param>
    /// <param name="databaseName">The database name.</param>
    public RavenDBStore(string connectionString, string? databaseName = null)
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
    public RavenDBStore(IDocumentStore documentStore)
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
    public override void Init()
    {
        EnsureDatabaseExists();
    }

    /// <inheritdoc />
    public override void Destroy()
    {
        var dbName = _documentStore?.Database;
        if (!string.IsNullOrEmpty(dbName))
        {
            _documentStore!.Maintenance.Server.SendAsync(
                new DeleteDatabasesOperation(dbName, hardDelete: true)
            ).GetAwaiter().GetResult();
        }
    }

    #endregion

    #region Core CRUD Operations - Single Item

    /// <inheritdoc />
    public override Guid Create(T data, StoreDataDelegate<T>? storeDelegate = null)
    {
        if (_documentStore == null || data == null) return Guid.Empty;

        data.Guid ??= Guid.NewGuid();
        storeDelegate?.Invoke(data);

        if (TransactionContext != null)
        {
            TransactionContext.Store(data);
            return data.Guid.Value;
        }

        using var session = _documentStore.OpenSession();
        session.Store(data);
        session.SaveChanges();

        return data.Guid.Value;
    }

    /// <inheritdoc />
    public override T? Read(Guid guid)
    {
        if (_documentStore == null || guid == Guid.Empty) return null;

        if (TransactionContext != null)
        {
            return TransactionContext.Load<T>(guid.ToString());
        }

        using var session = _documentStore.OpenSession();
        return session.Load<T>(guid.ToString());
    }

    /// <inheritdoc />
    public override IEnumerable<T> Read()
    {
        if (_documentStore == null) return Enumerable.Empty<T>();

        if (TransactionContext != null)
        {
            return TransactionContext.Query<T>().ToList();
        }

        using var session = _documentStore.OpenSession();
        return session.Query<T>().ToList();
    }

    /// <inheritdoc />
    public override T? Read(Expression<Func<T, bool>>? filter = null)
    {
        if (_documentStore == null) return null;

        if (TransactionContext != null)
        {
            if (filter != null)
            {
                return TransactionContext.Query<T>().FirstOrDefault(filter);
            }
            return TransactionContext.Query<T>().FirstOrDefault();
        }

        using var session = _documentStore.OpenSession();

        if (filter != null)
        {
            return session.Query<T>().FirstOrDefault(filter);
        }

        return session.Query<T>().FirstOrDefault();
    }

    /// <inheritdoc />
    public override void Update(T data, StoreDataDelegate<T>? storeDelegate = null)
    {
        if (_documentStore == null || data == null || data.Guid == null || data.Guid == Guid.Empty) return;

        storeDelegate?.Invoke(data);

        if (TransactionContext != null)
        {
            var existing = TransactionContext.Load<T>(data.Guid.Value.ToString());
            if (existing != null)
            {
                TransactionContext.Advanced.Evict(existing);
            }
            TransactionContext.Store(data);
            return;
        }

        using var session = _documentStore.OpenSession();
        var existingItem = session.Load<T>(data.Guid.Value.ToString());

        if (existingItem != null)
        {
            session.Advanced.Evict(existingItem);
        }

        session.Store(data);
        session.SaveChanges();
    }

    /// <inheritdoc />
    public override void Delete(T data)
    {
        if (_documentStore == null || data == null || data.Guid == null || data.Guid == Guid.Empty) return;

        if (TransactionContext != null)
        {
            TransactionContext.Delete(data.Guid.Value.ToString());
            return;
        }

        using var session = _documentStore.OpenSession();
        session.Delete(data.Guid.Value.ToString());
        session.SaveChanges();
    }

    #endregion

    #region Query and Count Operations

    /// <inheritdoc />
    public override long Count(Expression<Func<T, bool>>? filter = null)
    {
        if (_documentStore == null) return 0;

        if (TransactionContext != null)
        {
            if (filter != null)
            {
                return TransactionContext.Query<T>().Count(filter);
            }
            return TransactionContext.Query<T>().Count();
        }

        using var session = _documentStore.OpenSession();

        if (filter != null)
        {
            return session.Query<T>().Count(filter);
        }

        return session.Query<T>().Count();
    }

    #endregion

    #region Core CRUD Operations - Bulk

    /// <inheritdoc />
    public override IEnumerable<T> Read(Expression<Func<T, bool>>? filter = null, OrderBy<T>? orderBy = null, int? limit = null, int? offset = null)
    {
        if (_documentStore == null) return Enumerable.Empty<T>();

        var session = TransactionContext ?? _documentStore.OpenSession();
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

            var results = query.ToList();

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
    public override void Create(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null)
    {
        if (_documentStore == null || data == null) return;

        if (TransactionContext != null)
        {
            foreach (var item in data)
            {
                if (item == null) continue;
                item.Guid = Guid.NewGuid();
                storeDelegate?.Invoke(item);
                TransactionContext.Store(item);
            }
            return;
        }

        using var bulkInsert = _documentStore.BulkInsert();

        foreach (var item in data)
        {
            if (item == null) continue;

            item.Guid = Guid.NewGuid();
            storeDelegate?.Invoke(item);

            bulkInsert.Store(item);
        }
    }

    /// <inheritdoc />
    public override void Update(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null)
    {
        if (_documentStore == null || data == null) return;

        var session = TransactionContext ?? _documentStore.OpenSession();
        try
        {
            foreach (var item in data)
            {
                if (item == null || item.Guid == null || item.Guid == Guid.Empty)
                {
                    continue;
                }

                storeDelegate?.Invoke(item);

                var existing = session.Load<T>(item.Guid.Value.ToString());
                if (existing != null)
                {
                    session.Advanced.Evict(existing);
                }

                session.Store(item);
            }

            if (TransactionContext == null)
            {
                session.SaveChanges();
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
    public override void Delete(IEnumerable<T> data)
    {
        if (_documentStore == null || data == null) return;

        var session = TransactionContext ?? _documentStore.OpenSession();
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
                session.SaveChanges();
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

    #endregion
}
