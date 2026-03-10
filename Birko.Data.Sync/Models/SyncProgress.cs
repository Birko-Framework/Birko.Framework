using System;

namespace Birko.Data.Sync.Models;

/// <summary>
/// Current progress of a synchronization operation
/// </summary>
public class SyncProgress
{
    /// <summary>
    /// Total number of items to process
    /// </summary>
    public int TotalItems { get; set; }

    /// <summary>
    /// Number of items processed so far
    /// </summary>
    public int ProcessedItems { get; set; }

    /// <summary>
    /// Number of items created
    /// </summary>
    public int CreatedItems { get; set; }

    /// <summary>
    /// Number of items updated
    /// </summary>
    public int UpdatedItems { get; set; }

    /// <summary>
    /// Number of items deleted
    /// </summary>
    public int DeletedItems { get; set; }

    /// <summary>
    /// Number of items skipped
    /// </summary>
    public int SkippedItems { get; set; }

    /// <summary>
    /// Number of conflicts detected
    /// </summary>
    public int Conflicts { get; set; }

    /// <summary>
    /// Number of errors encountered
    /// </summary>
    public int Errors { get; set; }

    /// <summary>
    /// When the sync started
    /// </summary>
    public DateTime StartTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Current phase of synchronization
    /// </summary>
    public SyncPhase Phase { get; set; } = SyncPhase.NotStarted;

    /// <summary>
    /// Estimated time remaining
    /// </summary>
    public TimeSpan? EstimatedTimeRemaining { get; set; }

    /// <summary>
    /// Progress percentage (0-100)
    /// </summary>
    public int PercentComplete => TotalItems > 0 ? (int)((ProcessedItems / (double)TotalItems) * 100) : 0;
}

/// <summary>
/// Phase of synchronization operation
/// </summary>
public enum SyncPhase
{
    NotStarted,
    DetectingChanges,
    DownloadingChanges,
    UploadingChanges,
    ApplyingChanges,
    ResolvingConflicts,
    Completed,
    Failed
}
