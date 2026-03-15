using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Storage;

/// <summary>
/// Convenience extension methods for IFileStorage.
/// </summary>
public static class FileStorageExtensions
{
    /// <summary>
    /// Upload from a byte array.
    /// </summary>
    public static async Task<FileReference> UploadBytesAsync(
        this IFileStorage storage,
        string path,
        byte[] data,
        string contentType,
        StorageOptions? options = null,
        CancellationToken ct = default)
    {
        using var stream = new MemoryStream(data);
        return await storage.UploadAsync(path, stream, contentType, options, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Upload from a local file path.
    /// </summary>
    public static async Task<FileReference> UploadFileAsync(
        this IFileStorage storage,
        string storagePath,
        string localFilePath,
        string contentType,
        StorageOptions? options = null,
        CancellationToken ct = default)
    {
        using var stream = new FileStream(localFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        return await storage.UploadAsync(storagePath, stream, contentType, options, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Download as a byte array (suitable for small files).
    /// </summary>
    public static async Task<StorageResult<byte[]>> DownloadBytesAsync(
        this IFileStorage storage,
        string path,
        CancellationToken ct = default)
    {
        var result = await storage.DownloadAsync(path, ct).ConfigureAwait(false);
        if (!result.Found)
        {
            return StorageResult<byte[]>.NotFound();
        }

        using var stream = result.Value!;
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct).ConfigureAwait(false);
        return StorageResult<byte[]>.Success(ms.ToArray());
    }

    /// <summary>
    /// Download directly to a local file path.
    /// Returns true if the file was downloaded, false if not found.
    /// </summary>
    public static async Task<bool> DownloadToFileAsync(
        this IFileStorage storage,
        string storagePath,
        string localFilePath,
        CancellationToken ct = default)
    {
        var result = await storage.DownloadAsync(storagePath, ct).ConfigureAwait(false);
        if (!result.Found)
        {
            return false;
        }

        using var stream = result.Value!;
        var directory = Path.GetDirectoryName(localFilePath);
        if (directory != null)
        {
            Directory.CreateDirectory(directory);
        }

        using var fileStream = new FileStream(localFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
        await stream.CopyToAsync(fileStream, ct).ConfigureAwait(false);
        return true;
    }
}
