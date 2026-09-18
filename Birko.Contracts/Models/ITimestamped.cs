using System;

namespace Birko.Data.Models;

/// <summary>
/// Interface for entities that track creation and modification timestamps.
/// Implemented by AbstractLogModel for backward compatibility.
/// Use with TimestampStoreWrapper to automatically manage these fields.
/// </summary>
public interface ITimestamped
{
    /// <summary>
    /// When the entity was created.
    /// </summary>
    DateTime CreatedAt { get; set; }

    /// <summary>
    /// When the entity was last updated.
    /// </summary>
    DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Previous update timestamp (before the most recent update).
    /// </summary>
    DateTime? PrevUpdatedAt { get; set; }
}
