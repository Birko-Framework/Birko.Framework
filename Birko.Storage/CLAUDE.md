# Birko.Storage

## Overview
File and blob storage abstraction for the Birko Framework. Provides a unified `IFileStorage` interface with a built-in local filesystem implementation. Cloud providers (Azure Blob, AWS S3, GCS, MinIO) are separate projects.

## Location
`C:\Source\Birko.Storage\`

## Structure
```
Birko.Storage/
├── Core/
│   ├── IFileStorage.cs            — Main async interface (Upload/Download/Delete/Exists/GetReference/List/Copy/Move)
│   ├── IPresignedUrlStorage.cs    — Optional capability for cloud providers
│   ├── FileReference.cs           — File metadata (Path, FileName, ContentType, Size, CreatedAt, ETag, Metadata)
│   ├── StorageResult.cs           — Found/NotFound result wrapper (readonly struct)
│   ├── StorageSettings.cs         — Extends Birko.Data.Stores.Settings (Location=basePath, Name=logical name, PathPrefix)
│   ├── StorageOptions.cs          — Per-operation options (MaxFileSize, AllowedContentTypes, OverwriteExisting)
│   ├── PresignedUrlOptions.cs     — Presigned URL expiry and content options
│   └── StorageException.cs        — Exception hierarchy (FileAlreadyExists, FileTooLarge, ContentTypeNotAllowed, InvalidPath)
├── Local/
│   └── LocalFileStorage.cs        — Filesystem implementation with path sanitization and .meta.json companion files
└── Extensions/
    └── FileStorageExtensions.cs   — Convenience methods (UploadBytes, UploadFile, DownloadBytes, DownloadToFile)
```

## Dependencies
- **Birko.Data** — Settings/ISettings base classes

## Key Design Decisions
- **Async-first** — All operations return Task for cloud backend compatibility
- **Stream-based** — Core interface uses Stream; byte[] convenience via extensions
- **IPresignedUrlStorage separate** — Local can't do presigned URLs; avoids NotSupportedException
- **Path as string key** — Forward-slash separated, not filesystem paths. Local maps to files, cloud to blob keys
- **Metadata via .meta.json** — LocalFileStorage stores metadata in companion JSON files
- **Path security** — Rejects `..`, absolute paths, null bytes, control chars; resolved path must stay within base

## Usage
```csharp
var settings = new StorageSettings("/data/uploads", "main-storage");
using var storage = new LocalFileStorage(settings);

// Upload
using var stream = File.OpenRead("photo.jpg");
var reference = await storage.UploadAsync("products/photo.jpg", stream, "image/jpeg");

// Download
var result = await storage.DownloadAsync("products/photo.jpg");
if (result.Found) { using var s = result.Value; /* read stream */ }

// Extensions
var bytes = await storage.DownloadBytesAsync("products/photo.jpg");
```

## Maintenance
- Update README.md when adding new methods or changing behavior
- Update this CLAUDE.md when adding new files or changing architecture
- All new functionality must have corresponding unit tests in Birko.Storage.Tests
