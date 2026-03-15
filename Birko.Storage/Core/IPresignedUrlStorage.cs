using System;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Storage;

/// <summary>
/// Optional capability for storage backends that support presigned/SAS URLs.
/// Cloud providers implement this; local storage does not.
/// </summary>
public interface IPresignedUrlStorage
{
    /// <summary>
    /// Generates a presigned URL for downloading a file.
    /// </summary>
    Task<Uri> GetDownloadUrlAsync(
        string path,
        PresignedUrlOptions? options = null,
        CancellationToken ct = default);

    /// <summary>
    /// Generates a presigned URL for uploading a file.
    /// </summary>
    Task<Uri> GetUploadUrlAsync(
        string path,
        PresignedUrlOptions? options = null,
        CancellationToken ct = default);
}
