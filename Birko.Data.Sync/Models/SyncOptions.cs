using System;
using System.Threading;

namespace Birko.Data.Sync.Models;

/// <summary>
/// Options for synchronization operations
/// </summary>
public class SyncOptions
{
    /// <summary>
    /// Direction of synchronization
    /// </summary>
    public SyncDirection Direction { get; set; } = SyncDirection.Bidirectional;

    /// <summary>
    /// Conflict resolution policy
    /// </summary>
    public ConflictResolutionPolicy ConflictPolicy { get; set; } = ConflictResolutionPolicy.NewestWins;

    /// <summary>
    /// Custom conflict resolution function (used when ConflictPolicy is Custom)
    /// </summary>
    public Func<ConflictInfo, ConflictResolution>? CustomConflictResolver { get; set; }

    /// <summary>
    /// Number of items to process per batch
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// Maximum number of items to sync (null = unlimited)
    /// </summary>
    public int? MaxItems { get; set; }

    /// <summary>
    /// Scope of the sync (e.g., "Products", "Orders")
    /// </summary>
    public string Scope { get; set; } = "Default";

    /// <summary>
    /// Cancellation token
    /// </summary>
    public CancellationToken CancellationToken { get; set; }

    // Progress reporting callbacks

    /// <summary>
    /// Called when progress updates
    /// </summary>
    public Action<SyncProgress>? OnProgress { get; set; }

    /// <summary>
    /// Called when a conflict is detected
    /// </summary>
    public Action<ConflictInfo>? OnConflict { get; set; }

    /// <summary>
    /// Called when an error occurs
    /// </summary>
    public Action<SyncError>? OnError { get; set; }

    /// <summary>
    /// Called when a batch starts processing
    /// </summary>
    public Action<int>? OnBatchStarting { get; set; }

    /// <summary>
    /// Called when a batch completes
    /// </summary>
    public Action<SyncBatchResult>? OnBatchCompleted { get; set; }

    /// <summary>
    /// Reserved (CR-L207): a hint that a caller intends to execute directly without a preview. It is not
    /// consulted by the provider — <c>Preview</c>/<c>PreviewAsync</c> and <c>Sync</c>/<c>SyncAsync</c> are
    /// independent public entry points, so "skipping" the preview simply means not calling it. Kept for
    /// source compatibility and caller-side branching.
    /// </summary>
    public bool SkipPreview { get; set; } = false;
}
