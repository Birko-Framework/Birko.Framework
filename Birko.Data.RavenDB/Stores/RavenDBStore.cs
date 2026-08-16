using Birko.Data.Models;
using Birko.Data.RavenDB.Aggregation;
using Birko.Data.Stores;
using ISettings = Birko.Configuration.ISettings;
using Raven.Client.Documents;
using Raven.Client.Documents.Conventions;
using Raven.Client.Documents.Linq;
using Raven.Client.Documents.Operations.Indexes;
using Raven.Client.Documents.Queries.Facets;
using Raven.Client.ServerWide;
using Raven.Client.ServerWide.Operations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Birko.Data.RavenDB.Stores;

/// <summary>
/// RavenDB implementation of IBulkStore for document-based storage with bulk operations.
/// </summary>
public class RavenDBStore<T>
    : AbstractBulkStore<T>
    , ISettingsStore<Settings>
    , ITransactionalStore<T, Raven.Client.Documents.Session.IDocumentSession>
    , IAggregatableStore<T>
    , IDisposable
    where T : AbstractModel
{
    private IDocumentStore? _documentStore;
    protected Settings? _settings;
    // True when this store created the _documentStore (connection-string ctor or Settings.CreateDocumentStore)
    // and is therefore responsible for disposing it. An externally-supplied store is not owned.
    private bool _ownsStore;
    private bool _disposed;

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
        _ownsStore = true;
    }

    /// <summary>
    /// Initializes a new instance with an existing document store.
    /// The store is externally owned and will not be disposed by this class.
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
    /// <param name="settings">The RavenDB settings to use.</param>
    public virtual void SetSettings(Settings settings)
    {
        SetSettings((ISettings)settings);
    }

    /// <summary>
    /// Sets the connection settings via the ISettings interface.
    /// </summary>
    /// <param name="settings">The settings to use.</param>
    public virtual void SetSettings(ISettings settings)
    {
        if (settings is Settings ravenSettings)
        {
            _settings = ravenSettings;
            ReplaceDocumentStore(ravenSettings.CreateDocumentStore());
        }
        else if (settings is Birko.Configuration.RemoteSettings remote)
        {
            _settings = new Settings();
            _settings.LoadFrom(remote);
            ReplaceDocumentStore(_settings.CreateDocumentStore());
        }
    }

    /// <summary>
    /// Swaps in a newly-created (owned) document store, disposing the previously-owned one first so a
    /// repeated SetSettings does not leak the store created by the prior call.
    /// </summary>
    private void ReplaceDocumentStore(IDocumentStore newStore)
    {
        var previous = _ownsStore ? _documentStore : null;
        _documentStore = newStore;
        _ownsStore = true;
        if (!ReferenceEquals(previous, newStore))
        {
            previous?.Dispose();
        }
    }

    /// <inheritdoc />
    protected override void InitCore()
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
    protected override Guid CreateCore(T data, StoreDataDelegate<T>? storeDelegate = null)
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
        // Run the lazy-init gate the base public wrappers provide before the load-by-id fast path,
        // so a Read as the first operation still creates the database (CR-H077).
        EnsureInitialized();
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
        EnsureInitialized();
        if (_documentStore == null) return Enumerable.Empty<T>();

        if (TransactionContext != null)
        {
            return TransactionContext.Query<T>().ToList();
        }

        using var session = _documentStore.OpenSession();
        return session.Query<T>().ToList();
    }

    /// <inheritdoc />
    protected override T? ReadCore(Expression<Func<T, bool>>? filter = null)
    {
        // TASK-221: RavenDB's LINQ provider translates NO collection Contains, in any spelling —
        // only its own .In(). Rewrite the portable form where the caller's expression arrives, so a
        // filter that works on every other backend works here too. Only
        // constCollection.Contains(x.Member) is rewritten; x.CollectionMember.Contains(const) is the
        // opposite direction, already translates, and is left alone.
        filter = Expressions.RavenSetMembership.Rewrite(filter);
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
    protected override void UpdateCore(T data, StoreDataDelegate<T>? storeDelegate = null)
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
    protected override void DeleteCore(T data)
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
    protected override long CountCore(Expression<Func<T, bool>>? filter = null)
    {
        filter = Expressions.RavenSetMembership.Rewrite(filter);   // TASK-221 — see above
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
    protected override IEnumerable<T> ReadCore(Expression<Func<T, bool>>? filter = null, OrderBy<T>? orderBy = null, int? limit = null, int? offset = null)
    {
        filter = Expressions.RavenSetMembership.Rewrite(filter);   // TASK-221 — see above
        if (_documentStore == null) return Enumerable.Empty<T>();

        var session = TransactionContext ?? _documentStore.OpenSession();
        try
        {
            IRavenQueryable<T> query = session.Query<T>();

            if (filter != null)
            {
                query = query.Where(filter);
            }

            if (orderBy?.Fields.Count > 0)
            {
                IQueryable<T> sorted = query;
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

                    sorted = (IQueryable<T>)method.Invoke(null, new object[] { sorted, lambda })!;
                }
                query = (IRavenQueryable<T>)sorted;
            }

            if (offset.HasValue)
            {
                query = (IRavenQueryable<T>)query.Skip(offset.Value);
            }

            if (limit.HasValue)
            {
                query = (IRavenQueryable<T>)query.Take(limit.Value);
            }

            return query.ToList();
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
    protected override void CreateCore(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null)
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
    protected override void UpdateCore(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null)
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
    protected override void DeleteCore(IEnumerable<T> data)
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

    #region Aggregation

    /// <summary>
    /// Executes a synchronous aggregation query using native RavenDB faceted aggregation for single GROUP BY,
    /// with LINQ fallback for multi-field GROUP BY or time bucketing.
    /// </summary>
    public IReadOnlyList<AggregateResult> Aggregate(AggregateQuery<T> query)
    {
        if (_documentStore == null) return Array.Empty<AggregateResult>();

        var session = TransactionContext ?? _documentStore.OpenSession();
        try
        {
            bool canUseNative = query.GroupByFields.Count == 1
                && string.IsNullOrEmpty(query.TimeBucketInterval);

            if (canUseNative)
            {
                return NativeFacetAggregate(session, query);
            }

            IQueryable<T> q = session.Query<T>();
            if (query.Filter != null)
                q = q.Where(query.Filter);

            var data = q.ToList();
            return AggregateHelper.LinqAggregate(data, query);
        }
        finally
        {
            if (TransactionContext == null)
                session.Dispose();
        }
    }

    private IReadOnlyList<AggregateResult> NativeFacetAggregate(
        Raven.Client.Documents.Session.IDocumentSession session,
        AggregateQuery<T> query)
    {
        IQueryable<T> q = session.Query<T>();
        if (query.Filter != null)
            q = q.Where(query.Filter);

        var aggregation = q.AggregateBy(FacetAggregationHelper.BuildFacetBuilder(query));
        var facetResults = aggregation.Execute();

        var results = FacetAggregationHelper.MapFacetResults(facetResults, query);
        return AggregateHelper.ApplyOrderingAndPaging(results, query.OrderBy, query.Offset, query.Limit).AsReadOnly();
    }

    #endregion

    #region Health

    /// <summary>
    /// Checks if the RavenDB server is reachable by issuing a real (empty) query, mirroring the async
    /// store's IsHealthy. This is a genuine connectivity probe — unlike DatabaseExists(), which returns
    /// true for an empty database name without touching the server.
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

    /// <summary>
    /// Disposes the underlying document store only when this store owns it (created from a connection
    /// string or Settings.CreateDocumentStore). An externally-supplied store is left untouched.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ownsStore)
        {
            _documentStore?.Dispose();
        }
        GC.SuppressFinalize(this);
    }
}
