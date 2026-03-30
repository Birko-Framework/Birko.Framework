using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Birko.Storage;
using Birko.Storage.AzureBlob;
using Birko.Storage.Local;
using Birko.Time;

namespace Birko.Framework.Examples.Storage
{
    public static class StorageExamples
    {
        /// <summary>
        /// Core storage types: IFileStorage, FileReference, StorageResult, StorageOptions.
        /// </summary>
        public static void RunCoreTypesExample()
        {
            ExampleOutput.WriteLine("=== Storage Core Types ===\n");

            // StorageResult
            ExampleOutput.WriteHeader("StorageResult<T>");
            var found = StorageResult<string>.Success("hello");
            ExampleOutput.WriteInfo("Found", found.Found.ToString());
            ExampleOutput.WriteInfo("Value", found.Value!);

            var notFound = StorageResult<string>.NotFound();
            ExampleOutput.WriteInfo("Not Found", notFound.Found.ToString());
            ExampleOutput.WriteDim("  Value is default when not found — check .Found first");

            // FileReference
            ExampleOutput.WriteHeader("FileReference");
            var reference = new FileReference
            {
                Path = "products/photo.jpg",
                FileName = "photo.jpg",
                ContentType = "image/jpeg",
                Size = 204800,
                CreatedAt = DateTimeOffset.UtcNow,
                ETag = "abc123",
                Metadata = { ["author"] = "system" }
            };
            ExampleOutput.WriteInfo("Path", reference.Path);
            ExampleOutput.WriteInfo("ContentType", reference.ContentType);
            ExampleOutput.WriteInfo("Size", $"{reference.Size} bytes");
            ExampleOutput.WriteInfo("ETag", reference.ETag ?? "none");
            ExampleOutput.WriteDim($"  Metadata: {reference.Metadata.Count} entries");

            // StorageOptions
            ExampleOutput.WriteHeader("StorageOptions");
            var options = new StorageOptions
            {
                MaxFileSize = 10 * 1024 * 1024,
                AllowedContentTypes = new[] { "image/jpeg", "image/png", "application/pdf" },
                OverwriteExisting = false,
                Metadata = new System.Collections.Generic.Dictionary<string, string> { ["source"] = "upload" }
            };
            ExampleOutput.WriteInfo("MaxFileSize", $"{options.MaxFileSize / (1024 * 1024)} MB");
            ExampleOutput.WriteInfo("AllowedTypes", string.Join(", ", options.AllowedContentTypes!));
            ExampleOutput.WriteInfo("Overwrite", options.OverwriteExisting.ToString());

            // PresignedUrlOptions
            ExampleOutput.WriteHeader("PresignedUrlOptions");
            var presigned = new PresignedUrlOptions
            {
                Expiry = TimeSpan.FromHours(2),
                ContentType = "application/pdf",
                ContentDisposition = "attachment; filename=\"report.pdf\""
            };
            ExampleOutput.WriteInfo("Expiry", presigned.Expiry.ToString());
            ExampleOutput.WriteInfo("ContentType", presigned.ContentType ?? "any");

            ExampleOutput.WriteLine("\n=== Example Complete ===");
        }

        /// <summary>
        /// LocalFileStorage: upload, download, list, copy, move, delete.
        /// </summary>
        public static async Task RunLocalStorageExample()
        {
            ExampleOutput.WriteLine("=== Local File Storage ===\n");

            var tempDir = Path.Combine(Path.GetTempPath(), "birko-storage-demo-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(tempDir);

            try
            {
                var settings = new StorageSettings(tempDir, "demo-storage");
                using var storage = new LocalFileStorage(settings, new SystemDateTimeProvider());

                // Upload
                ExampleOutput.WriteHeader("Upload");
                var data = Encoding.UTF8.GetBytes("Hello, Birko Storage!");
                using var stream = new MemoryStream(data);
                var uploadRef = await storage.UploadAsync("docs/readme.txt", stream, "text/plain",
                    new StorageOptions { Metadata = new() { ["author"] = "demo" } });
                ExampleOutput.WriteSuccess($"Uploaded: {uploadRef.Path} ({uploadRef.Size} bytes)");
                ExampleOutput.WriteInfo("ETag", uploadRef.ETag ?? "none");
                ExampleOutput.WriteInfo("ContentType", uploadRef.ContentType);

                // Upload bytes (extension)
                var jsonData = Encoding.UTF8.GetBytes("{\"key\": \"value\"}");
                var jsonRef = await storage.UploadBytesAsync("data/config.json", jsonData, "application/json");
                ExampleOutput.WriteSuccess($"Uploaded: {jsonRef.Path} ({jsonRef.Size} bytes)");

                // Exists
                ExampleOutput.WriteHeader("Exists");
                var exists = await storage.ExistsAsync("docs/readme.txt");
                ExampleOutput.WriteInfo("docs/readme.txt", exists.ToString());
                var notExists = await storage.ExistsAsync("missing.txt");
                ExampleOutput.WriteInfo("missing.txt", notExists.ToString());

                // Download
                ExampleOutput.WriteHeader("Download");
                var result = await storage.DownloadBytesAsync("docs/readme.txt");
                if (result.Found)
                    ExampleOutput.WriteSuccess($"Content: {Encoding.UTF8.GetString(result.Value!)}");
                else
                    ExampleOutput.WriteError("Not found");

                // GetReference
                ExampleOutput.WriteHeader("Get Reference");
                var refResult = await storage.GetReferenceAsync("docs/readme.txt");
                if (refResult.Found)
                {
                    ExampleOutput.WriteInfo("Path", refResult.Value!.Path);
                    ExampleOutput.WriteInfo("Size", $"{refResult.Value.Size} bytes");
                    ExampleOutput.WriteInfo("Created", refResult.Value.CreatedAt.ToString("o"));
                    if (refResult.Value.Metadata.Count > 0)
                    {
                        foreach (var kv in refResult.Value.Metadata)
                            ExampleOutput.WriteDim($"  Metadata: {kv.Key} = {kv.Value}");
                    }
                }

                // List
                ExampleOutput.WriteHeader("List");
                var files = await storage.ListAsync();
                ExampleOutput.WriteInfo("Total files", files.Count.ToString());
                foreach (var f in files)
                    ExampleOutput.WriteDim($"  {f.Path} ({f.Size} bytes)");

                // List with prefix
                var docsFiles = await storage.ListAsync(prefix: "docs/");
                ExampleOutput.WriteInfo("Files in docs/", docsFiles.Count.ToString());

                // Copy
                ExampleOutput.WriteHeader("Copy");
                var copyRef = await storage.CopyAsync("docs/readme.txt", "archive/readme.txt");
                ExampleOutput.WriteSuccess($"Copied to: {copyRef.Path}");
                ExampleOutput.WriteInfo("Source exists", (await storage.ExistsAsync("docs/readme.txt")).ToString());

                // Move
                ExampleOutput.WriteHeader("Move");
                var moveRef = await storage.MoveAsync("data/config.json", "archive/config.json");
                ExampleOutput.WriteSuccess($"Moved to: {moveRef.Path}");
                ExampleOutput.WriteInfo("Source exists", (await storage.ExistsAsync("data/config.json")).ToString());

                // Delete
                ExampleOutput.WriteHeader("Delete");
                var deleted = await storage.DeleteAsync("archive/readme.txt");
                ExampleOutput.WriteInfo("Deleted", deleted.ToString());
                var deletedAgain = await storage.DeleteAsync("archive/readme.txt");
                ExampleOutput.WriteInfo("Delete again (no-op)", deletedAgain.ToString());
            }
            finally
            {
                // Cleanup
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }

            ExampleOutput.WriteLine("\n=== Example Complete ===");
        }

        /// <summary>
        /// Tenant isolation with PathPrefix.
        /// </summary>
        public static async Task RunTenantIsolationExample()
        {
            ExampleOutput.WriteLine("=== Tenant Isolation (PathPrefix) ===\n");

            var tempDir = Path.Combine(Path.GetTempPath(), "birko-storage-tenant-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(tempDir);

            try
            {
                // Tenant A
                var settingsA = new StorageSettings(tempDir, "storage", pathPrefix: "tenant-A");
                using var storageA = new LocalFileStorage(settingsA, new SystemDateTimeProvider());

                // Tenant B
                var settingsB = new StorageSettings(tempDir, "storage", pathPrefix: "tenant-B");
                using var storageB = new LocalFileStorage(settingsB, new SystemDateTimeProvider());

                ExampleOutput.WriteHeader("Upload to Both Tenants");
                await storageA.UploadBytesAsync("data.txt", Encoding.UTF8.GetBytes("Tenant A data"), "text/plain");
                ExampleOutput.WriteSuccess("Tenant A: uploaded data.txt");

                await storageB.UploadBytesAsync("data.txt", Encoding.UTF8.GetBytes("Tenant B data"), "text/plain");
                ExampleOutput.WriteSuccess("Tenant B: uploaded data.txt");

                ExampleOutput.WriteHeader("Isolation Check");
                var aFiles = await storageA.ListAsync();
                var bFiles = await storageB.ListAsync();
                ExampleOutput.WriteInfo("Tenant A files", aFiles.Count.ToString());
                ExampleOutput.WriteInfo("Tenant B files", bFiles.Count.ToString());

                var aResult = await storageA.DownloadBytesAsync("data.txt");
                var bResult = await storageB.DownloadBytesAsync("data.txt");
                ExampleOutput.WriteSuccess($"Tenant A content: {Encoding.UTF8.GetString(aResult.Value!)}");
                ExampleOutput.WriteSuccess($"Tenant B content: {Encoding.UTF8.GetString(bResult.Value!)}");
                ExampleOutput.WriteDim("  Same path 'data.txt' — different content per tenant");
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }

            ExampleOutput.WriteLine("\n=== Example Complete ===");
        }

        /// <summary>
        /// Error handling: size limits, content types, path validation.
        /// </summary>
        public static async Task RunErrorHandlingExample()
        {
            ExampleOutput.WriteLine("=== Storage Error Handling ===\n");

            var tempDir = Path.Combine(Path.GetTempPath(), "birko-storage-errors-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(tempDir);

            try
            {
                var settings = new StorageSettings(tempDir, "test");
                using var storage = new LocalFileStorage(settings, new SystemDateTimeProvider());

                // File too large
                ExampleOutput.WriteHeader("FileTooLargeException");
                try
                {
                    var bigData = new byte[1024];
                    using var bigStream = new MemoryStream(bigData);
                    await storage.UploadAsync("big.txt", bigStream, "text/plain",
                        new StorageOptions { MaxFileSize = 100 });
                }
                catch (FileTooLargeException ex)
                {
                    ExampleOutput.WriteSuccess($"Caught: {ex.GetType().Name}");
                    ExampleOutput.WriteDim($"  Size: {ex.FileSize}, Max: {ex.MaxSize}");
                }

                // Content type not allowed
                ExampleOutput.WriteHeader("ContentTypeNotAllowedException");
                try
                {
                    using var s = new MemoryStream(new byte[] { 1 });
                    await storage.UploadAsync("file.exe", s, "application/x-executable",
                        new StorageOptions { AllowedContentTypes = new[] { "image/jpeg", "image/png" } });
                }
                catch (ContentTypeNotAllowedException ex)
                {
                    ExampleOutput.WriteSuccess($"Caught: {ex.GetType().Name}");
                    ExampleOutput.WriteDim($"  ContentType: {ex.ContentType}");
                }

                // File already exists
                ExampleOutput.WriteHeader("FileAlreadyExistsException");
                try
                {
                    using var s1 = new MemoryStream(new byte[] { 1 });
                    await storage.UploadAsync("unique.txt", s1, "text/plain");
                    using var s2 = new MemoryStream(new byte[] { 2 });
                    await storage.UploadAsync("unique.txt", s2, "text/plain"); // no overwrite
                }
                catch (FileAlreadyExistsException ex)
                {
                    ExampleOutput.WriteSuccess($"Caught: {ex.GetType().Name}");
                    ExampleOutput.WriteDim($"  Path: {ex.StoragePath}");
                }

                // Path traversal
                ExampleOutput.WriteHeader("InvalidPathException");
                try
                {
                    using var s = new MemoryStream(new byte[] { 1 });
                    await storage.UploadAsync("../escape.txt", s, "text/plain");
                }
                catch (InvalidPathException ex)
                {
                    ExampleOutput.WriteSuccess($"Caught: {ex.GetType().Name}");
                    ExampleOutput.WriteDim($"  Path: {ex.StoragePath}");
                }

                // Not found (no exception — uses StorageResult)
                ExampleOutput.WriteHeader("Not Found (StorageResult)");
                var result = await storage.DownloadAsync("nonexistent.txt");
                ExampleOutput.WriteInfo("Found", result.Found.ToString());
                ExampleOutput.WriteDim("  No exception thrown — check .Found property");
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }

            ExampleOutput.WriteLine("\n=== Example Complete ===");
        }

        /// <summary>
        /// Azure Blob Storage: settings, presigned URLs, usage patterns.
        /// </summary>
        public static void RunAzureBlobExample()
        {
            ExampleOutput.WriteLine("=== Azure Blob Storage ===\n");

            ExampleOutput.WriteHeader("AzureBlobSettings");
            var settings = new AzureBlobSettings(
                storageAccountUri: "https://myaccount.blob.core.windows.net",
                containerName: "my-container",
                tenantId: "00000000-0000-0000-0000-000000000000",
                clientId: "app-client-id",
                clientSecret: "***",
                pathPrefix: "tenant-123/");
            ExampleOutput.WriteInfo("StorageAccountUri", settings.StorageAccountUri);
            ExampleOutput.WriteInfo("ContainerName", settings.ContainerName);
            ExampleOutput.WriteInfo("TenantId", settings.TenantId ?? "");
            ExampleOutput.WriteInfo("PathPrefix", settings.PathPrefix ?? "none");
            ExampleOutput.WriteInfo("Timeout", $"{settings.TimeoutSeconds}s");
            ExampleOutput.WriteDim("  Extends RemoteSettings — Location/Name/UserName/Password mapped to Azure fields");

            ExampleOutput.WriteHeader("AzureBlobStorage");
            ExampleOutput.WriteLine("  Implements IFileStorage + IPresignedUrlStorage");
            ExampleOutput.WriteDim("  Uses Azure Blob REST API directly — no Azure.Storage.Blobs SDK required");
            ExampleOutput.WriteDim("  OAuth2 client credentials (scope: https://storage.azure.com/.default)");
            ExampleOutput.WriteDim("  Token cached with SemaphoreSlim double-check pattern");
            ExampleOutput.WriteLine("");
            ExampleOutput.WriteLine("  Usage:");
            ExampleOutput.WriteDim("    using var storage = new AzureBlobStorage(settings);");
            ExampleOutput.WriteDim("    var ref = await storage.UploadAsync(\"docs/file.pdf\", stream, \"application/pdf\");");
            ExampleOutput.WriteDim("    var result = await storage.DownloadAsync(\"docs/file.pdf\");");
            ExampleOutput.WriteDim("    var files = await storage.ListAsync(prefix: \"docs/\", maxResults: 100);");

            ExampleOutput.WriteHeader("Presigned URLs (SAS Tokens)");
            ExampleOutput.WriteDim("  Requires AccountName + AccountKey on AzureBlobStorage instance:");
            ExampleOutput.WriteLine("");
            ExampleOutput.WriteDim("    storage.AccountName = \"myaccount\";");
            ExampleOutput.WriteDim("    storage.AccountKey = \"base64-account-key\";");
            ExampleOutput.WriteDim("    var downloadUrl = await storage.GetDownloadUrlAsync(\"docs/file.pdf\",");
            ExampleOutput.WriteDim("        new PresignedUrlOptions { Expiry = TimeSpan.FromHours(2) });");
            ExampleOutput.WriteDim("    var uploadUrl = await storage.GetUploadUrlAsync(\"uploads/new.pdf\");");
            ExampleOutput.WriteLine("");
            ExampleOutput.WriteDim("  SAS token signed with HMAC-SHA256, https-only, blob-scoped");

            ExampleOutput.WriteHeader("HttpClient Injection");
            ExampleOutput.WriteDim("  // Owned (default) — storage creates and disposes its own HttpClient");
            ExampleOutput.WriteDim("  var storage = new AzureBlobStorage(settings);");
            ExampleOutput.WriteDim("  // Injected — caller manages HttpClient lifetime");
            ExampleOutput.WriteDim("  var storage = new AzureBlobStorage(settings, httpClient);");

            ExampleOutput.WriteLine("\nNote: Live Azure demo requires a real Azure subscription.");
            ExampleOutput.WriteDim("  All IFileStorage methods are identical to LocalFileStorage.");

            ExampleOutput.WriteLine("\n=== Example Complete ===");
        }
    }
}
