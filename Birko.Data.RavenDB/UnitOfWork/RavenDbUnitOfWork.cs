using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Patterns.UnitOfWork;
using Raven.Client.Documents;
using Raven.Client.Documents.Session;

namespace Birko.Data.RavenDB.UnitOfWork;

/// <summary>
/// RavenDB Unit of Work wrapping IAsyncDocumentSession.
/// RavenDB sessions are inherently transactional — SaveChanges() commits all tracked changes atomically.
/// </summary>
public sealed class RavenDbUnitOfWork : IUnitOfWork<IAsyncDocumentSession>
{
    private readonly IDocumentStore _documentStore;
    private IAsyncDocumentSession? _session;
    private bool _disposed;

    public bool IsActive => _session is not null;
    public IAsyncDocumentSession? Context => _session;

    /// <inheritdoc />
    /// <remarks>
    /// The session is opened with <see cref="TransactionMode.ClusterWide"/>, so the boundary is a
    /// cluster-wide (compare-exchange backed) transaction rather than a single-node one. That is the
    /// stronger guarantee, and it is stated here because the two modes differ in what they permit:
    /// cluster-wide transactions do not support patching or attachments/counters/time-series, and
    /// conflicts surface as concurrency exceptions at SaveChanges rather than being merged.
    /// </remarks>
    public ITransactionCapabilities Capabilities { get; } = new TransactionCapabilities(
        TransactionAtomicity.Atomic,
        TransactionBoundaryScope.Cluster,
        // Measured against RavenDB 7.2, not assumed, and deliberately CONSERVATIVE.
        //
        // Raven splits here and a single bool cannot say both halves, so it says the one a caller is
        // unsafe to get wrong. ReadAsync(Guid) — Load-by-id — DOES see the session's unsaved writes as of
        // TASK-241, which aligned the document id with the entity Guid. Every other read does not: a
        // session query is answered by the server from indexes, so ReadAsync(filter), Read(), ReadFirst
        // and Count all return the pre-transaction state. Those are the common case, so declaring true
        // here would tell a caller its read-then-write logic is covered when usually it is not.
        //
        // The query half is a RavenDB property rather than a defect, and is not fixable from this side.
        readsSeeUncommittedWrites: false,
        limitations: "Cluster-wide transaction: no patching, attachments, counters or time-series inside "
                   + "the boundary; conflicts surface at SaveChanges as concurrency exceptions. Reads are "
                   + "split: Load-by-id (ReadAsync(Guid)) sees the session's unsaved writes, but every "
                   + "query-based read — ReadAsync(filter), Read(), ReadFirst, Count — is answered from "
                   + "server-side indexes and returns the pre-transaction state.");

    /// <summary>
    /// Creates a new RavenDbUnitOfWork from a document store.
    /// </summary>
    public RavenDbUnitOfWork(IDocumentStore documentStore)
    {
        _documentStore = documentStore ?? throw new ArgumentNullException(nameof(documentStore));
    }

    /// <summary>
    /// Creates a new RavenDbUnitOfWork from a configured store.
    /// </summary>
    public static RavenDbUnitOfWork FromStore<T>(Stores.AsyncRavenDBStore<T> store)
        where T : Data.Models.AbstractModel
    {
        var docStore = store.DocumentStore
            ?? throw new InvalidOperationException("Store DocumentStore is not initialized. Call SetSettings() first.");
        return new RavenDbUnitOfWork(docStore);
    }

    public Task BeginAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsActive)
            throw new TransactionAlreadyActiveException();

        _session = _documentStore.OpenAsyncSession(new SessionOptions
        {
            TransactionMode = TransactionMode.ClusterWide
        });
        return Task.CompletedTask;
    }

    public async Task CommitAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsActive)
            throw new NoActiveTransactionException();

        await _session!.SaveChangesAsync(ct);
        _session.Dispose();
        _session = null;
    }

    public Task RollbackAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsActive)
            throw new NoActiveTransactionException();

        // RavenDB sessions are transactional — unsaved changes are discarded on dispose.
        _session!.Dispose();
        _session = null;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            _session?.Dispose();
            _session = null;
        }
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _session?.Dispose();
            _session = null;
        }
    }
}
