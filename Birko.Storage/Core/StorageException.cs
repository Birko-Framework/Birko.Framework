using System;

namespace Birko.Storage;

/// <summary>
/// Base exception for storage operations.
/// </summary>
public class StorageException : Exception
{
    public string? StoragePath { get; }

    public StorageException(string message, string? path = null)
        : base(message)
    {
        StoragePath = path;
    }

    public StorageException(string message, Exception innerException, string? path = null)
        : base(message, innerException)
    {
        StoragePath = path;
    }
}

/// <summary>
/// The file already exists and OverwriteExisting is false.
/// </summary>
public class FileAlreadyExistsException : StorageException
{
    public FileAlreadyExistsException(string path)
        : base($"File already exists: {path}", path) { }
}

/// <summary>
/// The file exceeds the configured maximum size.
/// </summary>
public class FileTooLargeException : StorageException
{
    public long FileSize { get; }
    public long MaxSize { get; }

    public FileTooLargeException(string path, long fileSize, long maxSize)
        : base($"File size {fileSize} exceeds maximum {maxSize}: {path}", path)
    {
        FileSize = fileSize;
        MaxSize = maxSize;
    }
}

/// <summary>
/// The file's content type is not in the allowed list.
/// </summary>
public class ContentTypeNotAllowedException : StorageException
{
    public string ContentType { get; }

    public ContentTypeNotAllowedException(string path, string contentType)
        : base($"Content type '{contentType}' is not allowed: {path}", path)
    {
        ContentType = contentType;
    }
}

/// <summary>
/// The storage path contains invalid characters or traversal attempts.
/// </summary>
public class InvalidPathException : StorageException
{
    public InvalidPathException(string path)
        : base($"Invalid storage path: {path}", path) { }
}
