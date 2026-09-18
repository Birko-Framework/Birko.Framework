using System;
using System.Linq.Expressions;

namespace Birko.Data.Sync.Models;

/// <summary>
/// Filtering options for synchronization
/// </summary>
public class SyncFilterOptions
{
    /// <summary>
    /// Action to take when save filter blocks an item
    /// </summary>
    public SaveFilterBlockAction OnSaveFilterBlock { get; set; } = SaveFilterBlockAction.Skip;
}

/// <summary>
/// Filtering options for synchronization with typed filters
/// </summary>
public class SyncFilterOptions<T> : SyncFilterOptions
{
    /// <summary>
    /// Pre-sync filter: What to fetch from local side (predicate)
    /// </summary>
    public Expression<Func<T, bool>>? LocalFetchPredicate { get; set; }

    /// <summary>
    /// Pre-sync filter: What to fetch from remote side (predicate)
    /// </summary>
    public Expression<Func<T, bool>>? RemoteFetchPredicate { get; set; }

    /// <summary>
    /// Apply-time filter: Whether to allow saving to local
    /// </summary>
    public Func<T, bool>? CanSaveToLocal { get; set; }

    /// <summary>
    /// Apply-time filter: Whether to allow saving to remote
    /// </summary>
    public Func<T, bool>? CanSaveToRemote { get; set; }
}

/// <summary>
/// Action to take when save filter blocks an item that passed fetch filter
/// </summary>
public enum SaveFilterBlockAction
{
    /// <summary>
    /// Silently skip, log warning
    /// </summary>
    Skip,

    /// <summary>
    /// Log as error, continue
    /// </summary>
    LogAsError,

    /// <summary>
    /// Throw exception, fail sync
    /// </summary>
    ThrowException,

    /// <summary>
    /// Flag for manual review
    /// </summary>
    MarkConflict
}
