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
    private bool _disposed;

    public bool IsActive => _session?.IsInTransaction ?? false;
    public IClientSessionHandle? Context => _session;

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
    public MongoDbUnitOfWork(MongoDBClient client)
    {
        _client = client?.Client ?? throw new ArgumentNullException(nameof(client));
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

        _session = await _client.StartSessionAsync(cancellationToken: ct);
        _session.StartTransaction();
    }

    public async Task CommitAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsActive)
            throw new NoActiveTransactionException();

        await _session!.CommitTransactionAsync(ct);
    }

    public async Task RollbackAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsActive)
            throw new NoActiveTransactionException();

        await _session!.AbortTransactionAsync(ct);
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
