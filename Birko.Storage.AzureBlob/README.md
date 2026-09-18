# Birko.Storage.AzureBlob

Azure Blob Storage provider for the Birko.Storage abstraction. Implements `IFileStorage` and `IPresignedUrlStorage` using the Azure Blob Storage REST API directly — no Azure SDK dependency required.

## Features

- **Full IFileStorage implementation** — Upload, download, delete, exists, get reference, list, copy, move
- **Presigned URL support** — SAS token generation for secure download/upload URLs
- **OAuth2 authentication** — Azure AD client credentials with automatic token refresh
- **Tenant isolation** — PathPrefix support for multi-tenant blob separation
- **Custom metadata** — Per-blob metadata via `x-ms-meta-*` headers
- **Storage options** — MaxFileSize, AllowedContentTypes, OverwriteExisting, ContentDisposition
- **No SDK dependency** — Pure REST API implementation using System.Net.Http

## Usage

### Basic Setup

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

### Upload

```csharp
using var stream = File.OpenRead("photo.jpg");
var reference = await storage.UploadAsync(
    "products/photo.jpg",
    stream,
    "image/jpeg",
    new StorageOptions { OverwriteExisting = true });
```

### Download

```csharp
var result = await storage.DownloadAsync("products/photo.jpg");
if (result.Found)
{
    using var stream = result.Value!;
    // Process stream...
}
```

### Presigned URLs (requires account key)

```csharp
var storage = new AzureBlobStorage(settings)
{
    AccountName = "myaccount",
    AccountKey = "base64-account-key"
};

var downloadUrl = await storage.GetDownloadUrlAsync("products/photo.jpg",
    new PresignedUrlOptions { Expiry = TimeSpan.FromHours(2) });
```

### List Blobs

```csharp
var files = await storage.ListAsync(prefix: "products/", maxResults: 100);
foreach (var file in files)
{
    Console.WriteLine($"{file.Path} ({file.Size} bytes)");
}
```

### Copy and Move

```csharp
await storage.CopyAsync("products/photo.jpg", "archive/photo.jpg");
await storage.MoveAsync("temp/upload.pdf", "documents/report.pdf");
```

## Configuration

| Property | Description | Maps To |
|----------|-------------|---------|
| `StorageAccountUri` | Storage account base URL | `Settings.Location` |
| `ContainerName` | Azure container name | (new property) |
| `TenantId` | Azure AD tenant ID | `Settings.Name` |
| `ClientId` | Azure AD application ID | `RemoteSettings.UserName` |
| `ClientSecret` | Azure AD client secret | `PasswordSettings.Password` |
| `PathPrefix` | Prefix for tenant isolation | (new property) |
| `TimeoutSeconds` | HTTP timeout (default: 30) | (new property) |
| `DefaultOptions` | Default upload options | (new property) |

## Dependencies

- Birko.Storage (IFileStorage, IPresignedUrlStorage, core types)
- Birko.Data.Stores (RemoteSettings)

## License

[MIT](License.md)
