using System;

namespace Birko.Storage;

/// <summary>
/// Options for presigned URL generation.
/// </summary>
public class PresignedUrlOptions
{
    /// <summary>
    /// How long the URL remains valid. Default: 1 hour.
    /// </summary>
    public TimeSpan Expiry { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Content disposition header for download URLs.
    /// </summary>
    public string? ContentDisposition { get; set; }

    /// <summary>
    /// Content type constraint for upload URLs.
    /// </summary>
    public string? ContentType { get; set; }
}
