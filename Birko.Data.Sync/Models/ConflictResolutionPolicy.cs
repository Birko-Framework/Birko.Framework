namespace Birko.Data.Sync.Models;

/// <summary>
/// Policy for resolving sync conflicts
/// </summary>
public enum ConflictResolutionPolicy
{
    /// <summary>
    /// Local version always wins
    /// </summary>
    LocalWins,

    /// <summary>
    /// Remote version always wins
    /// </summary>
    RemoteWins,

    /// <summary>
    /// Version with the newest timestamp wins
    /// </summary>
    NewestWins,

    /// <summary>
    /// Use custom conflict resolution logic
    /// </summary>
    Custom
}
