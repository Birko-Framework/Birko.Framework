using Birko.Data.Patterns.UnitOfWork;

namespace Birko.Data.CosmosDB.Stores;

/// <summary>
/// Thrown when a write is added to a Cosmos transactional batch that is already pinned to a different
/// logical partition key.
/// </summary>
/// <remarks>
/// A <c>TransactionalBatch</c> is scoped to one logical partition — a boundary spanning two partitions
/// cannot exist, whatever the API lets a caller type. <see cref="AsyncCosmosDBStore{T}"/> derives an
/// item's partition key from its <c>Guid</c>, so in practice <b>every document is its own partition</b>
/// and any second entity in a batch trips this.
/// <para>
/// It derives from <see cref="UnitOfWorkException"/> so a host that already handles unit-of-work failures
/// keeps working, and it names both partition keys so the caller can see which write left the boundary
/// rather than being handed an opaque BadRequest from the server at commit time.
/// </para>
/// </remarks>
public class CosmosTransactionScopeException : UnitOfWorkException
{
    public CosmosTransactionScopeException(string batchPartitionKey, string attemptedPartitionKey)
        : base($"This Cosmos transactional batch is scoped to partition key '{batchPartitionKey}' and cannot "
             + $"also cover partition key '{attemptedPartitionKey}'. Cosmos DB transactional batches are "
             + "limited to a single logical partition; because this store partitions by entity Guid, a batch "
             + "can only ever cover one entity. Use a separate unit of work per partition, or commit the "
             + "current one before writing an entity with a different Guid.")
    {
        BatchPartitionKey = batchPartitionKey;
        AttemptedPartitionKey = attemptedPartitionKey;
    }

    /// <summary>The partition key the batch was already pinned to by an earlier write.</summary>
    public string BatchPartitionKey { get; }

    /// <summary>The partition key of the write that was refused.</summary>
    public string AttemptedPartitionKey { get; }
}
