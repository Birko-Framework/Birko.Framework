using System;

namespace Birko.Data.Sync.Models;

/// <summary>
/// Information about a sync conflict
/// </summary>
public class ConflictInfo
{
    /// <summary>
    /// GUID of the conflicted item
    /// </summary>
    public Guid Guid { get; set; }

    /// <summary>
    /// Local version information
    /// </summary>
    public object? LocalItem { get; set; }

    /// <summary>
    /// Remote version information
    /// </summary>
    public object? RemoteItem { get; set; }

    /// <summary>
    /// Reason for the conflict
    /// </summary>
    public string Reason { get; set; } = string.Empty;
}
