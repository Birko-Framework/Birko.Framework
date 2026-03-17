using Birko.Data.Models;
using System;

namespace Birko.Data.Aggregates.Mapping;

/// <summary>
/// Represents a single store operation to apply when expanding an aggregate
/// back into relational form (insert/update/delete on child or junction tables).
/// </summary>
public class SyncOperation
{
    /// <summary>
    /// The type of operation (Insert, Update, Delete).
    /// </summary>
    public SyncOperationType Type { get; }

    /// <summary>
    /// The entity type this operation targets.
    /// </summary>
    public Type EntityType { get; }

    /// <summary>
    /// The entity to operate on.
    /// </summary>
    public AbstractModel Entity { get; }

    /// <summary>
    /// Which navigation property (relationship) this operation belongs to.
    /// </summary>
    public string NavigationProperty { get; }

    public SyncOperation(SyncOperationType type, Type entityType, AbstractModel entity, string navigationProperty)
    {
        Type = type;
        EntityType = entityType ?? throw new ArgumentNullException(nameof(entityType));
        Entity = entity ?? throw new ArgumentNullException(nameof(entity));
        NavigationProperty = navigationProperty ?? throw new ArgumentNullException(nameof(navigationProperty));
    }
}
