using FluentAssertions;
using Xunit;

namespace Birko.Storage.AzureBlob.Tests.AzureBlob;

/// <summary>
/// Tests for path resolution, validation, and prefix handling in AzureBlobStorage.
/// These test the validation logic without making real HTTP calls.
/// </summary>
public class PathResolutionTests
{
    private readonly AzureBlobSettings _settings;

    public PathResolutionTests()
    {
        _settings = new AzureBlobSettings(
            "https://testaccount.blob.core.windows.net",
            "test-container",
            "tenant-id",
            "client-id",
            "client-secret");
    }

    [Fact]
    public async Task UploadAsync_PathTraversal_ThrowsInvalidPathException()
    {
        using var storage = new AzureBlobStorage(_settings);
        using var stream = new MemoryStream(new byte[] { 1 });

        var act = () => storage.UploadAsync("../escape.txt", stream, "text/plain");

        await act.Should().ThrowAsync<InvalidPathException>();
    }

    [Fact]
    public async Task UploadAsync_NullByteInPath_ThrowsInvalidPathException()
    {
        using var storage = new AzureBlobStorage(_settings);
        using var stream = new MemoryStream(new byte[] { 1 });

        var act = () => storage.UploadAsync("file\0.txt", stream, "text/plain");

        await act.Should().ThrowAsync<InvalidPathException>();
    }

    [Fact]
    public async Task UploadAsync_EmptyPath_ThrowsInvalidPathException()
    {
        using var storage = new AzureBlobStorage(_settings);
        using var stream = new MemoryStream(new byte[] { 1 });

        var act = () => storage.UploadAsync("", stream, "text/plain");

        await act.Should().ThrowAsync<InvalidPathException>();
    }

    [Fact]
    public async Task UploadAsync_WhitespacePath_ThrowsInvalidPathException()
    {
        using var storage = new AzureBlobStorage(_settings);
        using var stream = new MemoryStream(new byte[] { 1 });

        var act = () => storage.UploadAsync("   ", stream, "text/plain");

        await act.Should().ThrowAsync<InvalidPathException>();
    }

    [Fact]
    public async Task ExistsAsync_PathTraversal_ThrowsInvalidPathException()
    {
        using var storage = new AzureBlobStorage(_settings);

        var act = () => storage.ExistsAsync("../../etc/passwd");

        await act.Should().ThrowAsync<InvalidPathException>();
    }

    [Fact]
    public async Task DeleteAsync_PathTraversal_ThrowsInvalidPathException()
    {
        using var storage = new AzureBlobStorage(_settings);

        var act = () => storage.DeleteAsync("foo/../../../bar");

        await act.Should().ThrowAsync<InvalidPathException>();
    }

    [Fact]
    public void GetDownloadUrlAsync_PathTraversal_ThrowsInvalidPathException()
    {
        using var storage = new AzureBlobStorage(_settings)
        {
            AccountName = "test",
            AccountKey = "dGVzdGtleXRlc3RrZXl0ZXN0a2V5dGVzdGtleTE="
        };

        var act = () => storage.GetDownloadUrlAsync("../secret.txt");

        act.Should().ThrowAsync<InvalidPathException>();
    }

    [Fact]
    public async Task GetUploadUrlAsync_PathWithPrefix_IncludesPrefixInUri()
    {
        var settings = new AzureBlobSettings(
            "https://testaccount.blob.core.windows.net",
            "test-container",
            "tenant-id", "client-id", "client-secret",
            pathPrefix: "tenant-42/");

        using var storage = new AzureBlobStorage(settings)
        {
            AccountName = "testaccount",
            AccountKey = "dGVzdGtleXRlc3RrZXl0ZXN0a2V5dGVzdGtleTE="
        };

        var uri = await storage.GetUploadUrlAsync("docs/file.txt");

        uri.AbsolutePath.Should().Be("/test-container/tenant-42/docs/file.txt");
    }

    [Fact]
    public async Task GetDownloadUrlAsync_PathWithPrefixNoTrailingSlash_AddsSeparator()
    {
        var settings = new AzureBlobSettings(
            "https://testaccount.blob.core.windows.net",
            "test-container",
            "tenant-id", "client-id", "client-secret",
            pathPrefix: "tenant-42");

        using var storage = new AzureBlobStorage(settings)
        {
            AccountName = "testaccount",
            AccountKey = "dGVzdGtleXRlc3RrZXl0ZXN0a2V5dGVzdGtleTE="
        };

        var uri = await storage.GetDownloadUrlAsync("file.txt");

        uri.AbsolutePath.Should().Be("/test-container/tenant-42/file.txt");
    }

    [Fact]
    public async Task GetDownloadUrlAsync_NoPrefix_PathNotPrefixed()
    {
        using var storage = new AzureBlobStorage(_settings)
        {
            AccountName = "testaccount",
            AccountKey = "dGVzdGtleXRlc3RrZXl0ZXN0a2V5dGVzdGtleTE="
        };

        var uri = await storage.GetDownloadUrlAsync("products/photo.jpg");

        uri.AbsolutePath.Should().Be("/test-container/products/photo.jpg");
    }

    [Fact]
    public async Task GetDownloadUrlAsync_LeadingSlashStripped()
    {
        using var storage = new AzureBlobStorage(_settings)
        {
            AccountName = "testaccount",
            AccountKey = "dGVzdGtleXRlc3RrZXl0ZXN0a2V5dGVzdGtleTE="
        };

        var uri = await storage.GetDownloadUrlAsync("/products/photo.jpg");

        // Leading slash stripped, no double slash in path
        uri.AbsolutePath.Should().Be("/test-container/products/photo.jpg");
    }

    [Fact]
    public async Task GetDownloadUrlAsync_BackslashNormalized()
    {
        using var storage = new AzureBlobStorage(_settings)
        {
            AccountName = "testaccount",
            AccountKey = "dGVzdGtleXRlc3RrZXl0ZXN0a2V5dGVzdGtleTE="
        };

        var uri = await storage.GetDownloadUrlAsync("products\\images\\photo.jpg");

        uri.AbsolutePath.Should().Be("/test-container/products/images/photo.jpg");
    }
}
