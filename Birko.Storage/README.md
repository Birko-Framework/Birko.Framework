# Birko.Storage

File and blob storage abstraction for the Birko Framework.

## Features

- **Unified interface** — `IFileStorage` for all backends (local, cloud)
- **Async-first** — All operations are async with CancellationToken support
- **Stream-based** — Efficient for large files; byte[] convenience via extensions
- **Path security** — Rejects traversal attacks, absolute paths, control characters
- **Metadata support** — Custom key-value metadata per file
- **Presigned URLs** — Optional `IPresignedUrlStorage` interface for cloud providers
- **Local filesystem** — Built-in `LocalFileStorage` implementation

## Dependencies

- Birko.Data (for Settings base classes)

## Usage

### Basic Operations

```csharp
var settings = new StorageSettings("/data/uploads", "main-storage");
using var storage = new LocalFileStorage(settings);

// Upload a file
using var stream = File.OpenRead("photo.jpg");
var reference = await storage.UploadAsync("products/photo.jpg", stream, "image/jpeg");

// Download a file
var result = await storage.DownloadAsync("products/photo.jpg");
if (result.Found)
{
    using var downloadStream = result.Value;
    // Process stream...
}

// Check existence
bool exists = await storage.ExistsAsync("products/photo.jpg");

// Get metadata
var refResult = await storage.GetReferenceAsync("products/photo.jpg");
if (refResult.Found)
{
    Console.WriteLine($"Size: {refResult.Value.Size}, Type: {refResult.Value.ContentType}");
}

// List files
var files = await storage.ListAsync(prefix: "products/", maxResults: 50);

// Copy / Move
await storage.CopyAsync("products/photo.jpg", "archive/photo.jpg");
await storage.MoveAsync("temp/upload.pdf", "documents/invoice.pdf");

// Delete
bool deleted = await storage.DeleteAsync("products/photo.jpg");
```

### Extension Methods

```csharp
// Upload from bytes
await storage.UploadBytesAsync("data.json", jsonBytes, "application/json");

// Download as bytes
var bytesResult = await storage.DownloadBytesAsync("data.json");

// Download to local file
await storage.DownloadToFileAsync("report.pdf", "/tmp/report.pdf");

// Upload from local file
await storage.UploadFileAsync("documents/report.pdf", "/tmp/report.pdf", "application/pdf");
```

### Upload Options

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

### Tenant Isolation

```csharp
var settings = new StorageSettings("/data/uploads", "storage", pathPrefix: "tenant-123");
using var storage = new LocalFileStorage(settings);

// All paths are prefixed: "file.txt" → "tenant-123/file.txt"
await storage.UploadBytesAsync("file.txt", data, "text/plain");
```

## Related Projects

- **Birko.Storage.Azure** — Azure Blob Storage (planned)
- **Birko.Storage.Aws** — AWS S3 (planned)
- **Birko.Storage.Google** — Google Cloud Storage (planned)
- **Birko.Storage.Minio** — MinIO S3-compatible (planned)

## License

MIT License - see [License.md](License.md)
