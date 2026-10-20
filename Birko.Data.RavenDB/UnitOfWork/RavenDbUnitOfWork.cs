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
    private bool _endedByFailedCommit;
    // TASK-700: the database key its session is published under, the cell installed at construction so a session opened
    // in BeginAsync reaches the caller's flow, and the published entry.
    private readonly string? _ambientKey;
    private readonly IDisposable _cell;
    private Birko.Data.Stores.AmbientSession<IAsyncDocumentSession>.Scope? _published;
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
    /// <remarks>
    /// TASK-700: from <see cref="BeginAsync"/> to the transaction's end, the session is published for the document store's
    /// database (<c>RavenAmbientKey</c>) in the constructing flow, so an async store on that database that was given no
    /// session of its own writes inside it, as a SQL store joins a <c>SqlUnitOfWork</c>. Construct it in the flow that runs
    /// the writes. The sync store takes an <c>IDocumentSession</c> and does not join.
    /// </remarks>
    public RavenDbUnitOfWork(IDocumentStore documentStore)
    {
        _documentStore = documentStore ?? throw new ArgumentNullException(nameof(documentStore));
        _ambientKey = Stores.RavenAmbientKey.Of(documentStore);
        _cell = Birko.Data.Stores.AmbientSession<IAsyncDocumentSession>.InstallCell();
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
        _endedByFailedCommit = false;
        if (_ambientKey != null)
        {
            _published = Birko.Data.Stores.AmbientSession<IAsyncDocumentSession>.Enter(_ambientKey, _session);
        }
        return Task.CompletedTask;
    }

    /// <remarks>
    /// TASK-658: the session is released whatever the outcome, and a failed commit ends the boundary, as on every other
    /// backend (TASK-325, TASK-578). Measured: after a conflict the unit of work stayed active and every retry hit the
    /// same conflict; after a cancelled commit a retry wrote the changes late. The driver's exception propagates unchanged
    /// and the <see cref="RollbackAsync"/> that follows is a no-op. A caller that wants to try again begins anew and
    /// re-applies its changes, which is what a concurrency conflict needs anyway.
    /// </remarks>
    public async Task CommitAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsActive)
            throw new NoActiveTransactionException();
        // TASK-700: a write that joined this session and failed (a localized write boundary) marked it, so saving would
        // keep half of that write. The session stays open for RollbackAsync, as SqlUnitOfWork's transaction does.
        if (_published is { IsRollbackOnly: true })
            throw new TransactionRollbackOnlyException();

        Unpublish();
        var session = _session!;
        _session = null;
        try
        {
            await session.SaveChangesAsync(ct);
        }
        catch
        {
            _endedByFailedCommit = true;
            throw;
        }
        finally
        {
            session.Dispose();
        }
    }

    public Task RollbackAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsActive)
        {
            if (_endedByFailedCommit)
            {
                // Nothing left to discard: the failed commit already released the session.
                _endedByFailedCommit = false;
                return Task.CompletedTask;
            }
            throw new NoActiveTransactionException();
        }

        // RavenDB sessions are transactional — unsaved changes are discarded on dispose.
        Unpublish();
        _session!.Dispose();
        _session = null;
        return Task.CompletedTask;
    }

    private void Unpublish()
    {
        _published?.Dispose();
        _published = null;
    }

    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            Unpublish();
            _session?.Dispose();
            _session = null;
            _cell.Dispose();
        }
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            Unpublish();
            _session?.Dispose();
            _session = null;
            _cell.Dispose();
        }
    }
}
