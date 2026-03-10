namespace Birko.Data.Sync.Models;

/// <summary>
/// Direction of data synchronization
/// </summary>
public enum SyncDirection
{
    /// <summary>
    /// Download data from remote to local only
    /// </summary>
    Download,

    /// <summary>
    /// Upload data from local to remote only
    /// </summary>
    Upload,

    /// <summary>
    /// Synchronize data in both directions
    /// </summary>
    Bidirectional
}
