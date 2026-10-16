namespace Birko.Data.Sync.Models;

/// <summary>
/// Result of custom conflict resolution
/// </summary>
public enum ConflictResolution
{
    /// <summary>
    /// Use local version
    /// </summary>
    UseLocal,

    /// <summary>
    /// Use remote version
    /// </summary>
    UseRemote,

    /// <summary>
    /// Merge both versions. Not supported: nothing in the framework can merge two entities, so a resolver returning it
    /// fails the item with a <c>SyncError</c> (TASK-445). Return UseLocal, UseRemote or Skip instead.
    /// </summary>
    Merge,

    /// <summary>
    /// Skip this item
    /// </summary>
    Skip
}
