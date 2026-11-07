using System;
using Birko.Data.Exceptions;

namespace Birko.Data.CosmosDB.Stores;

/// <summary>
/// A filter-based update or delete whose IN list was split into chunks (TASK-834) failed after at least one chunk had
/// been applied. Cosmos DB has no transaction across partitions, so the applied chunks stay applied and the failing
/// chunk may be partly applied (the user's decision, 2026-11-07). The original failure is the inner exception.
/// </summary>
public sealed class CosmosChunkedWriteException : StoreException
{
    public CosmosChunkedWriteException(string operation, Type entityType, int chunksApplied, int chunkCount, Exception innerException)
        : base($"Cosmos DB {operation} of {entityType.Name} failed at chunk {chunksApplied + 1} of {chunkCount}: "
             + $"{chunksApplied} chunk(s) were applied and are not rolled back, and chunk {chunksApplied + 1} may be partly applied. "
             + $"Cause: {innerException.Message}", innerException)
    {
        Operation = operation;
        ChunksApplied = chunksApplied;
        ChunkCount = chunkCount;
    }

    /// <summary>"update" or "delete".</summary>
    public string Operation { get; }

    /// <summary>Chunks applied in full before the failing one.</summary>
    public int ChunksApplied { get; }

    /// <summary>Chunks the write was split into.</summary>
    public int ChunkCount { get; }
}
