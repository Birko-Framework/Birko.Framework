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
