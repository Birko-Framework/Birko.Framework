using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Patterns.UnitOfWork;
using Microsoft.Azure.Cosmos;

namespace Birko.Data.CosmosDB.UnitOfWork;

/// <summary>
/// Cosmos DB Unit of Work wrapping TransactionalBatch.
/// Cosmos DB transactional batches are scoped to a single partition key
/// and execute atomically — all operations succeed or all fail.
/// </summary>
public sealed class CosmosDbUnitOfWork : IUnitOfWork<TransactionalBatch>
{
    private readonly Container _container;
    private readonly PartitionKey _partitionKey;
    private TransactionalBatch? _batch;
    private bool _disposed;

    public bool IsActive => _batch is not null;
    public TransactionalBatch? Context => _batch;

    /// <summary>
    /// Creates a new CosmosDbUnitOfWork for a specific partition key.
    /// Cosmos DB transactional batches are scoped to a single partition.
    /// </summary>
    /// <param name="container">The Cosmos DB container.</param>
    /// <param name="partitionKey">The partition key for this transaction batch.</param>
    public CosmosDbUnitOfWork(Container container, PartitionKey partitionKey)
    {
        _container = container ?? throw new ArgumentNullException(nameof(container));
        _partitionKey = partitionKey;
    }

    /// <summary>
    /// Creates a new CosmosDbUnitOfWork from a configured async store.
    /// </summary>
    public static CosmosDbUnitOfWork FromStore<T>(Stores.AsyncCosmosDBStore<T> store, PartitionKey partitionKey)
        where T : Data.Models.AbstractModel
    {
        var container = store.Container
            ?? throw new InvalidOperationException("Store Container is not initialized. Call SetSettings() and InitAsync() first.");
        return new CosmosDbUnitOfWork(container, partitionKey);
    }

    public Task BeginAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsActive)
            throw new TransactionAlreadyActiveException();

        _batch = _container.CreateTransactionalBatch(_partitionKey);
        return Task.CompletedTask;
    }

    public async Task CommitAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsActive)
            throw new NoActiveTransactionException();

        using var response = await _batch!.ExecuteAsync(ct);
        _batch = null;

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Cosmos DB transactional batch failed with status {response.StatusCode}.");
        }
    }

    public Task RollbackAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsActive)
            throw new NoActiveTransactionException();

        // Cosmos DB batches are not committed until ExecuteAsync — just discard the batch.
        _batch = null;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            _batch = null;
        }
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _batch = null;
        }
    }
}
