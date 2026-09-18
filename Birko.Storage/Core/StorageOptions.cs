using System.Collections.Generic;

namespace Birko.Storage;

/// <summary>
/// Options for individual upload/copy/move operations.
/// </summary>
public class StorageOptions
{
    /// <summary>
    /// Maximum allowed file size in bytes. Null = no limit.
    /// </summary>
    public long? MaxFileSize { get; set; }

    /// <summary>
    /// Allowed content types. Null or empty = all types allowed.
    /// </summary>
    public IReadOnlyList<string>? AllowedContentTypes { get; set; }

    /// <summary>
    /// Whether to overwrite an existing file at the same path. Default: false.
    /// </summary>
    public bool OverwriteExisting { get; set; }

    /// <summary>
    /// Custom metadata to attach to the file.
    /// </summary>
    public Dictionary<string, string>? Metadata { get; set; }

    /// <summary>
    /// Content disposition (e.g., "attachment; filename=\"report.pdf\"").
    /// </summary>
    public string? ContentDisposition { get; set; }

    /// <summary>
    /// Default options: no size limit, all types allowed, no overwrite.
    /// </summary>
    public static StorageOptions Default => new();
}
