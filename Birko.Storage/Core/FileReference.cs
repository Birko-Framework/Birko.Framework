using System;
using System.Collections.Generic;

namespace Birko.Storage;

/// <summary>
/// Metadata about a stored file.
/// </summary>
public sealed class FileReference
{
    /// <summary>
    /// The storage path/key of the file (forward-slash separated).
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// The original file name (without directory).
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// MIME content type (e.g., "image/jpeg", "application/pdf").
    /// </summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>
    /// File size in bytes.
    /// </summary>
    public long Size { get; set; }

    /// <summary>
    /// When the file was created/uploaded.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// When the file was last modified.
    /// </summary>
    public DateTimeOffset? LastModifiedAt { get; set; }

    /// <summary>
    /// ETag or content hash for cache validation / integrity.
    /// </summary>
    public string? ETag { get; set; }

    /// <summary>
    /// Custom metadata key-value pairs.
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new();
}
