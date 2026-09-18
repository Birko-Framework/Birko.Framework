namespace Birko.Data.Sync.Models;

/// <summary>
/// Action to be taken during sync
/// </summary>
public enum SyncAction
{
    /// <summary>
    /// Create new item
    /// </summary>
    Create,

    /// <summary>
    /// Update existing item
    /// </summary>
    Update,

    /// <summary>
    /// Delete item
    /// </summary>
    Delete,

    /// <summary>
    /// Skip this item
    /// </summary>
    Skip,

    /// <summary>
    /// Conflict detected
    /// </summary>
    Conflict
}
