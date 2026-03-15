using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Storage;

/// <summary>
/// Unified file/blob storage interface for all backends (local, S3, Azure, GCS).
/// Paths are forward-slash-separated keys (e.g., "products/images/photo.jpg").
/// </summary>
public interface IFileStorage : IDisposable
{
    /// <summary>
    /// Uploads a file from a stream. Returns a reference to the stored file.
    /// </summary>
    Task<FileReference> UploadAsync(
        string path,
        Stream content,
        string contentType,
        StorageOptions? options = null,
        CancellationToken ct = default);

    /// <summary>
    /// Downloads a file as a readable stream.
    /// The caller is responsible for disposing the returned stream.
    /// Returns StorageResult.NotFound() if the file does not exist.
    /// </summary>
    Task<StorageResult<Stream>> DownloadAsync(
        string path,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes a file. Returns true if the file existed and was deleted.
    /// </summary>
    Task<bool> DeleteAsync(
        string path,
        CancellationToken ct = default);

    /// <summary>
    /// Checks whether a file exists at the given path.
    /// </summary>
    Task<bool> ExistsAsync(
        string path,
        CancellationToken ct = default);

    /// <summary>
    /// Gets metadata for a file without downloading its content.
    /// Returns StorageResult.NotFound() if the file does not exist.
    /// </summary>
    Task<StorageResult<FileReference>> GetReferenceAsync(
        string path,
        CancellationToken ct = default);

    /// <summary>
    /// Lists files matching an optional prefix (folder-like filtering).
    /// </summary>
    Task<IReadOnlyList<FileReference>> ListAsync(
        string? prefix = null,
        int? maxResults = null,
        CancellationToken ct = default);

    /// <summary>
    /// Copies a file from one path to another within the same storage.
    /// </summary>
    Task<FileReference> CopyAsync(
        string sourcePath,
        string destinationPath,
        StorageOptions? options = null,
        CancellationToken ct = default);

    /// <summary>
    /// Moves a file from one path to another within the same storage.
    /// </summary>
    Task<FileReference> MoveAsync(
        string sourcePath,
        string destinationPath,
        StorageOptions? options = null,
        CancellationToken ct = default);
}
