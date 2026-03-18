using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Birko.Helpers;
using Birko.Time;

namespace Birko.Storage.Local;

/// <summary>
/// Local filesystem implementation of IFileStorage.
/// Uses Settings.Location as the base directory.
/// Stores custom metadata in companion .meta.json files.
/// </summary>
public sealed class LocalFileStorage : IFileStorage
{
    private const string MetaSuffix = ".meta.json";

    private readonly StorageSettings _settings;
    private readonly IDateTimeProvider _clock;
    private readonly string _basePath;

    public LocalFileStorage(StorageSettings settings, IDateTimeProvider clock)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));

        if (string.IsNullOrWhiteSpace(settings.Location))
        {
            throw new ArgumentException("StorageSettings.Location (base directory) must be set.", nameof(settings));
        }

        _basePath = Path.GetFullPath(settings.Location);
    }

    public async Task<FileReference> UploadAsync(
        string path,
        Stream content,
        string contentType,
        StorageOptions? options = null,
        CancellationToken ct = default)
    {
        var resolvedPath = ResolvePath(path);
        var effectiveOptions = MergeOptions(options);

        ValidateContentType(path, contentType, effectiveOptions);

        if (!effectiveOptions.OverwriteExisting && File.Exists(resolvedPath))
        {
            throw new FileAlreadyExistsException(path);
        }

        var directory = Path.GetDirectoryName(resolvedPath);
        if (directory != null)
        {
            Directory.CreateDirectory(directory);
        }

        if (effectiveOptions.MaxFileSize.HasValue && content.CanSeek)
        {
            if (content.Length > effectiveOptions.MaxFileSize.Value)
            {
                throw new FileTooLargeException(path, content.Length, effectiveOptions.MaxFileSize.Value);
            }
        }

        long size;
        string etag;

        using (var fileStream = new FileStream(resolvedPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
        {
            if (effectiveOptions.MaxFileSize.HasValue && !content.CanSeek)
            {
                size = await CopyWithLimitAsync(content, fileStream, effectiveOptions.MaxFileSize.Value, path, ct).ConfigureAwait(false);
            }
            else
            {
                await content.CopyToAsync(fileStream, ct).ConfigureAwait(false);
                size = fileStream.Length;
            }
        }

        etag = await ComputeETagAsync(resolvedPath, ct).ConfigureAwait(false);

        var now = _clock.OffsetUtcNow;
        var metadata = effectiveOptions.Metadata ?? new Dictionary<string, string>();

        var reference = new FileReference
        {
            Path = PathValidator.NormalizePath(path),
            FileName = Path.GetFileName(path),
            ContentType = contentType,
            Size = size,
            CreatedAt = now,
            LastModifiedAt = now,
            ETag = etag,
            Metadata = new Dictionary<string, string>(metadata)
        };

        await SaveMetadataAsync(resolvedPath, reference, ct).ConfigureAwait(false);

        return reference;
    }

    public Task<StorageResult<Stream>> DownloadAsync(
        string path,
        CancellationToken ct = default)
    {
        var resolvedPath = ResolvePath(path);

        if (!File.Exists(resolvedPath))
        {
            return Task.FromResult(StorageResult<Stream>.NotFound());
        }

        Stream stream = new FileStream(resolvedPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        return Task.FromResult(StorageResult<Stream>.Success(stream));
    }

    public Task<bool> DeleteAsync(
        string path,
        CancellationToken ct = default)
    {
        var resolvedPath = ResolvePath(path);

        if (!File.Exists(resolvedPath))
        {
            return Task.FromResult(false);
        }

        File.Delete(resolvedPath);

        var metaPath = resolvedPath + MetaSuffix;
        if (File.Exists(metaPath))
        {
            File.Delete(metaPath);
        }

        return Task.FromResult(true);
    }

    public Task<bool> ExistsAsync(
        string path,
        CancellationToken ct = default)
    {
        var resolvedPath = ResolvePath(path);
        return Task.FromResult(File.Exists(resolvedPath));
    }

    public async Task<StorageResult<FileReference>> GetReferenceAsync(
        string path,
        CancellationToken ct = default)
    {
        var resolvedPath = ResolvePath(path);

        if (!File.Exists(resolvedPath))
        {
            return StorageResult<FileReference>.NotFound();
        }

        var reference = await LoadReferenceAsync(resolvedPath, path, ct).ConfigureAwait(false);
        return StorageResult<FileReference>.Success(reference);
    }

    public Task<IReadOnlyList<FileReference>> ListAsync(
        string? prefix = null,
        int? maxResults = null,
        CancellationToken ct = default)
    {
        if (!Directory.Exists(_basePath))
        {
            return Task.FromResult<IReadOnlyList<FileReference>>(Array.Empty<FileReference>());
        }

        var searchPath = _basePath;
        var effectivePrefix = CombinePrefix(prefix);

        if (!string.IsNullOrEmpty(effectivePrefix))
        {
            var prefixDir = Path.Combine(_basePath, effectivePrefix.Replace('/', Path.DirectorySeparatorChar));
            var dirPart = Path.GetDirectoryName(prefixDir);
            if (dirPart != null && Directory.Exists(dirPart))
            {
                searchPath = dirPart;
            }
        }

        if (!Directory.Exists(searchPath))
        {
            return Task.FromResult<IReadOnlyList<FileReference>>(Array.Empty<FileReference>());
        }

        var files = Directory.EnumerateFiles(searchPath, "*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(MetaSuffix, StringComparison.OrdinalIgnoreCase))
            .Select(f => ToStoragePath(f))
            .Where(p => string.IsNullOrEmpty(effectivePrefix) || p.StartsWith(effectivePrefix, StringComparison.OrdinalIgnoreCase));

        if (maxResults.HasValue)
        {
            files = files.Take(maxResults.Value);
        }

        var results = files.Select(p =>
        {
            var resolved = Path.Combine(_basePath, p.Replace('/', Path.DirectorySeparatorChar));
            var info = new FileInfo(resolved);
            return new FileReference
            {
                Path = p,
                FileName = info.Name,
                ContentType = string.Empty,
                Size = info.Exists ? info.Length : 0,
                CreatedAt = info.Exists ? new DateTimeOffset(info.CreationTimeUtc, TimeSpan.Zero) : DateTimeOffset.MinValue,
                LastModifiedAt = info.Exists ? new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero) : null
            };
        }).ToList();

        return Task.FromResult<IReadOnlyList<FileReference>>(results);
    }

    public async Task<FileReference> CopyAsync(
        string sourcePath,
        string destinationPath,
        StorageOptions? options = null,
        CancellationToken ct = default)
    {
        var resolvedSource = ResolvePath(sourcePath);
        var resolvedDest = ResolvePath(destinationPath);
        var effectiveOptions = MergeOptions(options);

        if (!File.Exists(resolvedSource))
        {
            throw new StorageException($"Source file not found: {sourcePath}", sourcePath);
        }

        if (!effectiveOptions.OverwriteExisting && File.Exists(resolvedDest))
        {
            throw new FileAlreadyExistsException(destinationPath);
        }

        var directory = Path.GetDirectoryName(resolvedDest);
        if (directory != null)
        {
            Directory.CreateDirectory(directory);
        }

        File.Copy(resolvedSource, resolvedDest, effectiveOptions.OverwriteExisting);

        var etag = await ComputeETagAsync(resolvedDest, ct).ConfigureAwait(false);
        var info = new FileInfo(resolvedDest);

        var sourceRef = await LoadMetadataAsync(resolvedSource, ct).ConfigureAwait(false);

        var reference = new FileReference
        {
            Path = PathValidator.NormalizePath(destinationPath),
            FileName = Path.GetFileName(destinationPath),
            ContentType = sourceRef?.ContentType ?? string.Empty,
            Size = info.Length,
            CreatedAt = _clock.OffsetUtcNow,
            LastModifiedAt = _clock.OffsetUtcNow,
            ETag = etag,
            Metadata = effectiveOptions.Metadata != null
                ? new Dictionary<string, string>(effectiveOptions.Metadata)
                : sourceRef?.Metadata != null
                    ? new Dictionary<string, string>(sourceRef.Metadata)
                    : new Dictionary<string, string>()
        };

        await SaveMetadataAsync(resolvedDest, reference, ct).ConfigureAwait(false);

        return reference;
    }

    public async Task<FileReference> MoveAsync(
        string sourcePath,
        string destinationPath,
        StorageOptions? options = null,
        CancellationToken ct = default)
    {
        var reference = await CopyAsync(sourcePath, destinationPath, options, ct).ConfigureAwait(false);
        await DeleteAsync(sourcePath, ct).ConfigureAwait(false);
        return reference;
    }

    public void Dispose()
    {
        // No resources to release for local filesystem
    }

    #region Path Resolution

    private string ResolvePath(string path)
    {
        try
        {
            PathValidator.ValidateUserPath(path, nameof(path));
        }
        catch (ArgumentException)
        {
            throw new InvalidPathException(path ?? string.Empty);
        }

        var normalized = PathValidator.NormalizePath(path);
        var withPrefix = CombinePrefix(normalized) ?? normalized;
        var systemPath = withPrefix.Replace('/', Path.DirectorySeparatorChar);
        var resolved = Path.GetFullPath(Path.Combine(_basePath, systemPath));

        if (!resolved.StartsWith(_basePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidPathException(path);
        }

        return resolved;
    }

    private string? CombinePrefix(string? path)
    {
        if (string.IsNullOrEmpty(_settings.PathPrefix))
        {
            return path;
        }

        var prefix = PathValidator.NormalizePath(_settings.PathPrefix!);
        if (!prefix.EndsWith('/'))
        {
            prefix += "/";
        }

        return string.IsNullOrEmpty(path) ? prefix : prefix + path;
    }

    private string ToStoragePath(string fullPath)
    {
        var relative = fullPath.Substring(_basePath.Length).TrimStart(Path.DirectorySeparatorChar, '/');
        return relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    #endregion

    #region Validation

    private static void ValidateContentType(string path, string contentType, StorageOptions options)
    {
        if (options.AllowedContentTypes != null && options.AllowedContentTypes.Count > 0)
        {
            if (!options.AllowedContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase))
            {
                throw new ContentTypeNotAllowedException(path, contentType);
            }
        }
    }

    private StorageOptions MergeOptions(StorageOptions? options)
    {
        if (options != null)
        {
            return options;
        }

        return _settings.DefaultOptions ?? StorageOptions.Default;
    }

    #endregion

    #region Metadata

    private static async Task SaveMetadataAsync(string filePath, FileReference reference, CancellationToken ct)
    {
        var metaPath = filePath + MetaSuffix;
        var json = JsonSerializer.Serialize(new FileMetadata
        {
            ContentType = reference.ContentType,
            CreatedAt = reference.CreatedAt,
            LastModifiedAt = reference.LastModifiedAt,
            ETag = reference.ETag,
            Metadata = reference.Metadata
        }, FileMetadataContext.Default.FileMetadata);

        await File.WriteAllTextAsync(metaPath, json, ct).ConfigureAwait(false);
    }

    private static async Task<FileReference?> LoadMetadataAsync(string filePath, CancellationToken ct)
    {
        var metaPath = filePath + MetaSuffix;
        if (!File.Exists(metaPath))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(metaPath, ct).ConfigureAwait(false);
        var meta = JsonSerializer.Deserialize(json, FileMetadataContext.Default.FileMetadata);
        if (meta == null)
        {
            return null;
        }

        return new FileReference
        {
            ContentType = meta.ContentType ?? string.Empty,
            CreatedAt = meta.CreatedAt,
            LastModifiedAt = meta.LastModifiedAt,
            ETag = meta.ETag,
            Metadata = meta.Metadata ?? new Dictionary<string, string>()
        };
    }

    private async Task<FileReference> LoadReferenceAsync(string resolvedPath, string storagePath, CancellationToken ct)
    {
        var info = new FileInfo(resolvedPath);
        var stored = await LoadMetadataAsync(resolvedPath, ct).ConfigureAwait(false);

        return new FileReference
        {
            Path = PathValidator.NormalizePath(storagePath),
            FileName = info.Name,
            ContentType = stored?.ContentType ?? string.Empty,
            Size = info.Length,
            CreatedAt = stored?.CreatedAt ?? new DateTimeOffset(info.CreationTimeUtc, TimeSpan.Zero),
            LastModifiedAt = stored?.LastModifiedAt ?? new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
            ETag = stored?.ETag,
            Metadata = stored?.Metadata ?? new Dictionary<string, string>()
        };
    }

    #endregion

    #region Helpers

    private static async Task<string> ComputeETagAsync(string filePath, CancellationToken ct)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        var hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static async Task<long> CopyWithLimitAsync(Stream source, Stream destination, long maxSize, string path, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long totalBytes = 0;
        int bytesRead;

        while ((bytesRead = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            totalBytes += bytesRead;
            if (totalBytes > maxSize)
            {
                throw new FileTooLargeException(path, totalBytes, maxSize);
            }

            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), ct).ConfigureAwait(false);
        }

        return totalBytes;
    }

    #endregion

    #region Metadata Model

    internal sealed class FileMetadata
    {
        public string? ContentType { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? LastModifiedAt { get; set; }
        public string? ETag { get; set; }
        public Dictionary<string, string>? Metadata { get; set; }
    }

    #endregion
}

/// <summary>
/// Source-generated JSON context for metadata serialization.
/// </summary>
[System.Text.Json.Serialization.JsonSerializable(typeof(LocalFileStorage.FileMetadata))]
internal partial class FileMetadataContext : System.Text.Json.Serialization.JsonSerializerContext
{
}
