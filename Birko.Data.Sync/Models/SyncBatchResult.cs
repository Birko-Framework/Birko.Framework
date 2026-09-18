using System.Collections.Generic;

namespace Birko.Data.Sync.Models;

/// <summary>
/// Result of a batch synchronization
/// </summary>
public class SyncBatchResult
{
    /// <summary>
    /// Batch number
    /// </summary>
    public int BatchNumber { get; set; }

    /// <summary>
    /// Number of items processed in this batch
    /// </summary>
    public int Processed { get; set; }

    /// <summary>
    /// Errors that occurred in this batch
    /// </summary>
    public List<SyncError> Errors { get; set; } = new();
}
