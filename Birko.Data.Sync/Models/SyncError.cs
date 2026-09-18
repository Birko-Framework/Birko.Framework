using System;

namespace Birko.Data.Sync.Models;

/// <summary>
/// Error that occurred during synchronization
/// </summary>
public class SyncError
{
    /// <summary>
    /// GUID of the item that failed
    /// </summary>
    public Guid? ItemGuid { get; set; }

    /// <summary>
    /// Operation that failed
    /// </summary>
    public string? Operation { get; set; }

    /// <summary>
    /// Error message
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Detailed error information
    /// </summary>
    public string? Details { get; set; }

    /// <summary>
    /// The exception if one occurred
    /// </summary>
    public Exception? Exception { get; set; }

    /// <summary>
    /// When the error occurred
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
