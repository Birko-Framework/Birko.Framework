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
    /// Merge both versions (if supported)
    /// </summary>
    Merge,

    /// <summary>
    /// Skip this item
    /// </summary>
    Skip
}
