using System;

namespace Birko.Data.Patterns.Models;

/// <summary>
/// Interface for entities that support soft deletion.
/// When deleted, the entity is marked with a timestamp instead of being removed.
/// </summary>
public interface ISoftDeletable
{
    /// <summary>
    /// When the entity was soft-deleted. Null means active (not deleted).
    /// </summary>
    DateTime? DeletedAt { get; set; }
}
