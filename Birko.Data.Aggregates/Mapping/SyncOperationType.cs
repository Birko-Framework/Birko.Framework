namespace Birko.Data.Aggregates.Mapping;

/// <summary>
/// Type of store operation to apply when expanding an aggregate back to relational form.
/// </summary>
public enum SyncOperationType
{
    /// <summary>
    /// Insert a new entity.
    /// </summary>
    Insert,

    /// <summary>
    /// Update an existing entity.
    /// </summary>
    Update,

    /// <summary>
    /// Delete an existing entity.
    /// </summary>
    Delete
}
