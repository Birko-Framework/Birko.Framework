using System;

namespace Birko.Data.Sync.Models;

/// <summary>
/// Preview of a single item's sync action
/// </summary>
public class SyncItemPreview
{
    /// <summary>
    /// GUID of the item
    /// </summary>
    public Guid Guid { get; set; }

    /// <summary>
    /// Action that will be taken
    /// </summary>
    public SyncAction Action { get; set; }

    /// <summary>
    /// Reason for this action
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Conflict information (if applicable)
    /// </summary>
    public ConflictInfo? Conflict { get; set; }

    /// <summary>
    /// Local version info
    /// </summary>
    public string? LocalVersion { get; set; }

    /// <summary>
    /// Remote version info
    /// </summary>
    public string? RemoteVersion { get; set; }
}
