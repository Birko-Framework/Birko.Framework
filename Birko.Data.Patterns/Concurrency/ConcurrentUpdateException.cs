using System;

namespace Birko.Data.Patterns.Concurrency;

/// <summary>
/// Thrown when an optimistic concurrency conflict is detected —
/// the entity has been modified by another process since it was read.
/// </summary>
public class ConcurrentUpdateException : Exception
{
    /// <summary>
    /// The type of the entity that had a concurrency conflict.
    /// </summary>
    public Type? EntityType { get; }

    /// <summary>
    /// The identifier of the entity that had a concurrency conflict.
    /// </summary>
    public Guid? EntityId { get; }

    /// <summary>
    /// The version the caller expected.
    /// </summary>
    public long? ExpectedVersion { get; }

    public ConcurrentUpdateException()
        : base("The entity has been modified by another process.") { }

    public ConcurrentUpdateException(string message)
        : base(message) { }

    public ConcurrentUpdateException(string message, Exception innerException)
        : base(message, innerException) { }

    public ConcurrentUpdateException(Type entityType, Guid entityId, long expectedVersion)
        : base($"Concurrency conflict on {entityType.Name} (Id: {entityId}). Expected version {expectedVersion} but the entity has been modified.")
    {
        EntityType = entityType;
        EntityId = entityId;
        ExpectedVersion = expectedVersion;
    }
}
