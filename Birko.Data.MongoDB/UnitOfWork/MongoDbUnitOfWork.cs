using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Patterns.UnitOfWork;
using MongoDB.Driver;

namespace Birko.Data.MongoDB.UnitOfWork;

/// <summary>
/// MongoDB Unit of Work using client sessions for multi-document transactions.
/// Requires a replica set or sharded cluster for transaction support.
/// </summary>
public sealed class MongoDbUnitOfWork : IUnitOfWork<IClientSessionHandle>
{
    private readonly IMongoClient _client;
    private IClientSessionHandle? _session;
    private bool _endedByFailedCommit;
    // TASK-700: the database key its session is published under (Birko clients only), the cell installed at
    // construction so a session entered in BeginAsync reaches the caller's flow, and the published entry.
    private readonly string? _ambientKey;
    private readonly IDisposable? _cell;
    private Birko.Data.Stores.AmbientSession<IClientSessionHandle>.Scope? _published;
    private bool _disposed;

    public bool IsActive => _session?.IsInTransaction ?? false;
    public IClientSessionHandle? Context => _session;

    /// <inheritdoc />
    /// <remarks>
    /// Multi-document transactions require a replica set or a sharded cluster. Against a standalone
    /// <c>mongod</c> <see cref="BeginAsync"/> succeeds and the first write fails at runtime — which is why
    /// <see cref="ITransactionCapabilities.RequiresServerTopology"/> is stated rather than assumed.
    /// Reads issued through <c>AsyncMongoDBStore</c> inside the session see the session's own uncommitted
    /// writes (TASK-240); before that fix they silently bypassed it.
    /// </remarks>
    public ITransactionCapabilities Capabilities { get; } = new TransactionCapabilities(
        TransactionAtomicity.Atomic,
        TransactionBoundaryScope.Cluster,
        readsSeeUncommittedWrites: true,
        requiresServerTopology: true,
        limitations: "Requires a replica set or sharded cluster. Against a standalone mongod the "
                   + "transaction fails at first write, not at BeginAsync.");

    /// <summary>
    /// Creates a new MongoDbUnitOfWork from a MongoClient.
    /// </summary>
    public MongoDbUnitOfWork(IMongoClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <summary>
    /// Creates a new MongoDbUnitOfWork from a Birko MongoDBClient.
    /// </summary>
    /// <remarks>
    /// TASK-700: from <see cref="BeginAsync"/> to the transaction's end, the session is published for the client's
    /// database in the constructing flow, so a store on that database that was given no context of its own writes
    /// inside it, as a SQL store joins a <c>SqlUnitOfWork</c>. Construct it in the flow that runs the writes.
    /// </remarks>
    public MongoDbUnitOfWork(MongoDBClient client)
    {
        _client = client?.Client ?? throw new ArgumentNullException(nameof(client));
        _ambientKey = client.Settings.GetId();
        _cell = Birko.Data.Stores.AmbientSession<IClientSessionHandle>.InstallCell();
    }

    /// <summary>
    /// Creates a new MongoDbUnitOfWork from a configured store.
    /// </summary>
    public static MongoDbUnitOfWork FromStore<T>(Stores.AsyncMongoDBStore<T> store)
        where T : Data.Models.AbstractModel
    {
        var client = store.Client
            ?? throw new InvalidOperationException("Store client is not initialized. Call SetSettings() first.");
        return new MongoDbUnitOfWork(client);
    }

    public async Task BeginAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsActive)
            throw new TransactionAlreadyActiveException();

        ReleaseSession();
        _session = await _client.StartSessionAsync(cancellationToken: ct);
        _session.StartTransaction();
        _endedByFailedCommit = false;
        if (_ambientKey != null)
        {
            _published = Birko.Data.Stores.AmbientSession<IClientSessionHandle>.Enter(_ambientKey, _session);
        }
    }

    /// <remarks>
    /// TASK-578 (SH-M429): the session is disposed when the transaction ends, by commit, abort or a failed commit. It
    /// used to stay held, so the next <see cref="BeginAsync"/> started a new session over the undisposed one. A failed
    /// commit ends the boundary as on SQL and Elasticsearch (TASK-325): the driver's exception propagates unchanged,
    /// disposing the session aborts what it did not commit, and the <see cref="RollbackAsync"/> that follows is a no-op.
    /// </remarks>
    public async Task CommitAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsActive)
            throw new NoActiveTransactionException();
        // TASK-700: a write that joined this transaction and failed (a localized write boundary) marked it, so committing
        // would keep half of that write. The transaction stays active for RollbackAsync, as SqlUnitOfWork's does.
        if (_published is { IsRollbackOnly: true })
            throw new TransactionRollbackOnlyException();

        try
        {
            await _session!.CommitTransactionAsync(ct);
        }
        catch
        {
            _endedByFailedCommit = true;
            throw;
        }
        finally
        {
            ReleaseSession();
        }
    }

    public async Task RollbackAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsActive)
        {
            if (_endedByFailedCommit)
            {
                // Nothing left to abort: the failed commit already released the session, which aborted it.
                _endedByFailedCommit = false;
                return;
            }
            throw new NoActiveTransactionException();
        }

        try
        {
            await _session!.AbortTransactionAsync(ct);
        }
        finally
        {
            ReleaseSession();
        }
    }

    private void ReleaseSession()
    {
        _published?.Dispose();
        _published = null;
        var session = _session;
        _session = null;
        try { session?.Dispose(); }
        catch { /* releasing must not replace the exception that ended the transaction */ }
    }

    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            _published?.Dispose();
            _session?.Dispose();
            _session = null;
            _cell?.Dispose();
        }
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _published?.Dispose();
            _session?.Dispose();
            _session = null;
            _cell?.Dispose();
        }
    }
}
