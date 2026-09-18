using System.Collections.Generic;

namespace Birko.Data.Sync.Models;

/// <summary>
/// Preview of synchronization changes
/// </summary>
public class SyncPreview
{
    /// <summary>
    /// Number of items to be created
    /// </summary>
    public int ToCreate { get; set; }

    /// <summary>
    /// Number of items to be updated
    /// </summary>
    public int ToUpdate { get; set; }

    /// <summary>
    /// Number of items to be deleted
    /// </summary>
    public int ToDelete { get; set; }

    /// <summary>
    /// Number of conflicts detected
    /// </summary>
    public int Conflicts { get; set; }

    /// <summary>
    /// Number of items to be skipped
    /// </summary>
    public int Skipped { get; set; }

    /// <summary>
    /// Detailed list of items and their planned actions
    /// </summary>
    public List<SyncItemPreview> Items { get; set; } = new();

    /// <summary>
    /// Scope of the sync preview
    /// </summary>
    public string Scope { get; set; } = string.Empty;
}
