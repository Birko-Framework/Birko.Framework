# Storage Guide

## Overview

Birko.Storage provides a unified `IFileStorage` interface for file and blob storage across all backends (local filesystem, cloud providers). Async-first, stream-based, with built-in path security.

## Core Interface

### IFileStorage

```csharp
public interface IFileStorage : IDisposable
{
    Task<FileReference> UploadAsync(string path, Stream content, string contentType,
        StorageOptions? options = null, CancellationToken ct = default);
    Task<StorageResult<Stream>> DownloadAsync(string path, CancellationToken ct = default);
    Task<bool> DeleteAsync(string path, CancellationToken ct = default);
    Task<bool> ExistsAsync(string path, CancellationToken ct = default);
    Task<StorageResult<FileReference>> GetReferenceAsync(string path, CancellationToken ct = default);
    Task<IReadOnlyList<FileReference>> ListAsync(string? prefix = null, int? maxResults = null,
        CancellationToken ct = default);
    Task<FileReference> CopyAsync(string sourcePath, string destinationPath,
        StorageOptions? options = null, CancellationToken ct = default);
    Task<FileReference> MoveAsync(string sourcePath, string destinationPath,
        StorageOptions? options = null, CancellationToken ct = default);
}
```

### FileReference

Metadata returned from upload, list, and get operations:

```csharp
var ref = await storage.GetReferenceAsync("products/photo.jpg");
if (ref.Found)
{
    Console.WriteLine($"Path: {ref.Value.Path}");
    Console.WriteLine($"Size: {ref.Value.Size}");
    Console.WriteLine($"Type: {ref.Value.ContentType}");
    Console.WriteLine($"Created: {ref.Value.CreatedAt}");
    Console.WriteLine($"ETag: {ref.Value.ETag}");
}
```

### StorageResult\<T\>

Distinguishes "not found" from a successful result:

```csharp
var result = await storage.DownloadAsync("file.txt");
if (result.Found)
{
    using var stream = result.Value;
    // process stream
}
```

## Local Filesystem

Built-in `LocalFileStorage` implementation:

```csharp
var settings = new StorageSettings("/data/uploads", "main-storage");
using var storage = new LocalFileStorage(settings);

// Upload
using var stream = File.OpenRead("photo.jpg");
var reference = await storage.UploadAsync("products/photo.jpg", stream, "image/jpeg");

// Download
var result = await storage.DownloadAsync("products/photo.jpg");
if (result.Found)
{
    using var s = result.Value;
    // read stream
}

// List files by prefix
var files = await storage.ListAsync(prefix: "products/", maxResults: 50);

// Copy / Move
await storage.CopyAsync("products/photo.jpg", "archive/photo.jpg");
await storage.MoveAsync("temp/upload.pdf", "documents/invoice.pdf");
```

### Metadata

LocalFileStorage stores metadata in companion `.meta.json` files alongside each stored file.

## Extension Methods

Convenience methods for common operations:

```csharp
// Upload/download bytes
await storage.UploadBytesAsync("data.json", jsonBytes, "application/json");
var bytes = await storage.DownloadBytesAsync("data.json");

// Upload/download local files
await storage.UploadFileAsync("docs/report.pdf", "/tmp/report.pdf", "application/pdf");
await storage.DownloadToFileAsync("docs/report.pdf", "/tmp/report.pdf");
```

## Upload Options

```csharp
var options = new StorageOptions
{
    MaxFileSize = 10 * 1024 * 1024,  // 10 MB
    AllowedContentTypes = new[] { "image/jpeg", "image/png", "application/pdf" },
    OverwriteExisting = false,
    Metadata = new Dictionary<string, string> { ["author"] = "system" }
};

await storage.UploadAsync("file.jpg", stream, "image/jpeg", options);
```

## Tenant Isolation

Use `PathPrefix` to scope all operations to a tenant:

```csharp
var settings = new StorageSettings("/data/uploads", "storage", pathPrefix: "tenant-123");
using var storage = new LocalFileStorage(settings);

// All paths are prefixed: "file.txt" -> "tenant-123/file.txt"
await storage.UploadBytesAsync("file.txt", data, "text/plain");
```

## Presigned URLs

Optional `IPresignedUrlStorage` interface for cloud providers:

```csharp
if (storage is IPresignedUrlStorage presigned)
{
    var downloadUrl = await presigned.GetDownloadUrlAsync("file.pdf",
        new PresignedUrlOptions { Expiry = TimeSpan.FromHours(1) });
    var uploadUrl = await presigned.GetUploadUrlAsync("uploads/new.pdf",
        new PresignedUrlOptions { ContentType = "application/pdf" });
}
```

LocalFileStorage does not implement this — cloud providers implement both `IFileStorage` and `IPresignedUrlStorage`.

## Azure Blob Storage

`Birko.Storage.AzureBlob` — Azure Blob Storage provider using REST API directly (no Azure SDK dependency).

### Setup

```csharp
var settings = new AzureBlobSettings(
    storageAccountUri: "https://myaccount.blob.core.windows.net",
    containerName: "my-container",
    tenantId: "your-tenant-id",
    clientId: "your-client-id",
    clientSecret: "your-client-secret",
    pathPrefix: "tenant-123/");

using var storage = new AzureBlobStorage(settings);
```

Authentication uses OAuth2 client credentials (scope: `https://storage.azure.com/.default`). Tokens are cached and refreshed automatically.

### Usage

All `IFileStorage` methods work identically to LocalFileStorage:

```csharp
// Upload
using var stream = File.OpenRead("photo.jpg");
var reference = await storage.UploadAsync("products/photo.jpg", stream, "image/jpeg");

// Download
var result = await storage.DownloadAsync("products/photo.jpg");
if (result.Found) { using var s = result.Value; /* read */ }

// List, Copy, Move, Delete — same as LocalFileStorage
var files = await storage.ListAsync(prefix: "products/", maxResults: 100);
```

### Presigned URLs (SAS Tokens)

Requires storage account key for HMAC-SHA256 signing:

```csharp
storage.AccountName = "myaccount";
storage.AccountKey = "base64-account-key";

var downloadUrl = await storage.GetDownloadUrlAsync("docs/file.pdf",
    new PresignedUrlOptions { Expiry = TimeSpan.FromHours(2) });

var uploadUrl = await storage.GetUploadUrlAsync("uploads/new.pdf",
    new PresignedUrlOptions { ContentType = "application/pdf" });
```

### HttpClient Injection

```csharp
// Owned (default) — creates and disposes its own HttpClient
var storage = new AzureBlobStorage(settings);

// Injected — caller manages lifetime
var storage = new AzureBlobStorage(settings, httpClient);
```

### Settings Mapping

| AzureBlobSettings | RemoteSettings | Purpose |
|-------------------|---------------|---------|
| `StorageAccountUri` | `Location` | Storage account base URL |
| `TenantId` | `Name` | Azure AD tenant ID |
| `ClientId` | `UserName` | Application ID |
| `ClientSecret` | `Password` | Client secret |
| `ContainerName` | (new) | Azure container name |
| `PathPrefix` | (new) | Tenant isolation prefix |
| `TimeoutSeconds` | (new) | HTTP timeout (default: 30) |

## Path Security

All paths are validated against traversal attacks:
- Rejects `..` segments, absolute paths, null bytes, control characters
- Resolved path must stay within the configured base directory

## Error Handling

| Exception | When |
|-----------|------|
| `StorageException` | Base exception |
| `FileAlreadyExistsException` | File exists and `OverwriteExisting` is false |
| `FileTooLargeException` | File exceeds `MaxFileSize` |
| `ContentTypeNotAllowedException` | Content type not in `AllowedContentTypes` |
| `InvalidPathException` | Path contains traversal or invalid characters |

## Provider Projects

| Project | Backend | Status |
|---------|---------|--------|
| `Birko.Storage.AzureBlob` | Azure Blob Storage | Implemented |
| `Birko.Storage.Aws` | AWS S3 | Planned |
| `Birko.Storage.Google` | Google Cloud Storage | Planned |
| `Birko.Storage.Minio` | MinIO (S3-compatible) | Planned |

## See Also

- [Birko.Storage](https://github.com/birko/Birko.Storage)
- [Birko.Storage.AzureBlob](https://github.com/birko/Birko.Storage.AzureBlob)
