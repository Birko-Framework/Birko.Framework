using System;
using System.Collections.Generic;

namespace Birko.Data.Sync.Models;

/// <summary>
/// Result of a synchronization operation
/// </summary>
public class SyncResult
{
    /// <summary>
    /// Whether the sync completed successfully
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Total number of items processed
    /// </summary>
    public int TotalProcessed { get; set; }

    /// <summary>
    /// Number of items created
    /// </summary>
    public int Created { get; set; }

    /// <summary>
    /// Number of items updated
    /// </summary>
    public int Updated { get; set; }

    /// <summary>
    /// Number of items deleted
    /// </summary>
    public int Deleted { get; set; }

    /// <summary>
    /// Number of items skipped
    /// </summary>
    public int Skipped { get; set; }

    /// <summary>
    /// Number of conflicts resolved
    /// </summary>
    public int Conflicts { get; set; }

    /// <summary>
    /// List of errors that occurred
    /// </summary>
    public List<SyncError> Errors { get; set; } = new();

    /// <summary>
    /// Duration of the sync operation
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// When the sync started
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// When the sync completed
    /// </summary>
    public DateTime EndTime { get; set; }

    /// <summary>
    /// Scope of the sync (e.g., "Products", "Orders")
    /// </summary>
    public string Scope { get; set; } = string.Empty;

    /// <summary>
    /// Sync direction used
    /// </summary>
    public SyncDirection Direction { get; set; }

    /// <summary>
    /// Whether this was an initial sync
    /// </summary>
    public bool IsInitialSync { get; set; }
}
