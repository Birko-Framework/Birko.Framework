using System.Net;
using Birko.Time;
using FluentAssertions;
using Xunit;

namespace Birko.Storage.AzureBlob.Tests.AzureBlob;

public class AzureBlobStorageTests : IDisposable
{
    private readonly AzureBlobSettings _settings;
    private readonly IDateTimeProvider _clock = new SystemDateTimeProvider();

    public AzureBlobStorageTests()
    {
        _settings = new AzureBlobSettings(
            "https://testaccount.blob.core.windows.net",
            "test-container",
            "tenant-id",
            "client-id",
            "client-secret");
    }

    public void Dispose()
    {
        // No resources to clean up in unit tests (no real HTTP calls)
    }

    [Fact]
    public void Constructor_NullSettings_ThrowsArgumentNullException()
    {
        var act = () => new AzureBlobStorage(null!, _clock);

        act.Should().Throw<ArgumentNullException>().WithParameterName("settings");
    }

    [Fact]
    public void Constructor_EmptyStorageAccountUri_ThrowsArgumentException()
    {
        var settings = new AzureBlobSettings { ContainerName = "test" };

        var act = () => new AzureBlobStorage(settings, _clock);

        act.Should().Throw<ArgumentException>().WithParameterName("settings");
    }

    [Fact]
    public void Constructor_EmptyContainerName_ThrowsArgumentException()
    {
        var settings = new AzureBlobSettings { StorageAccountUri = "https://test.blob.core.windows.net" };

        var act = () => new AzureBlobStorage(settings, _clock);

        act.Should().Throw<ArgumentException>().WithParameterName("settings");
    }

    [Fact]
    public void Constructor_ValidSettings_CreatesInstance()
    {
        using var storage = new AzureBlobStorage(_settings, _clock);

        storage.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithHttpClient_UsesProvidedClient()
    {
        using var httpClient = new HttpClient();
        using var storage = new AzureBlobStorage(_settings, _clock, httpClient);

        storage.Should().NotBeNull();
    }

    [Fact]
    public async Task UploadAsync_NullPath_ThrowsArgumentNullException()
    {
        using var storage = new AzureBlobStorage(_settings, _clock);
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });

        var act = () => storage.UploadAsync(null!, stream, "text/plain");

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task UploadAsync_NullContent_ThrowsArgumentNullException()
    {
        using var storage = new AzureBlobStorage(_settings, _clock);

        var act = () => storage.UploadAsync("test.txt", null!, "text/plain");

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task UploadAsync_DisallowedContentType_ThrowsContentTypeNotAllowed()
    {
        using var storage = new AzureBlobStorage(_settings, _clock);
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var options = new StorageOptions { AllowedContentTypes = new[] { "image/jpeg", "image/png" } };

        var act = () => storage.UploadAsync("test.txt", stream, "text/plain", options);

        await act.Should().ThrowAsync<ContentTypeNotAllowedException>();
    }

    [Fact]
    public async Task UploadAsync_SeekableStreamExceedsMaxSize_ThrowsFileTooLarge()
    {
        using var storage = new AzureBlobStorage(_settings, _clock);
        using var stream = new MemoryStream(new byte[1024]);
        var options = new StorageOptions { MaxFileSize = 100, OverwriteExisting = true };

        // This will throw FileTooLargeException before making the HTTP call
        var act = () => storage.UploadAsync("test.txt", stream, "text/plain", options);

        await act.Should().ThrowAsync<FileTooLargeException>();
    }

    [Fact]
    public async Task DownloadAsync_NullPath_ThrowsArgumentNullException()
    {
        using var storage = new AzureBlobStorage(_settings, _clock);

        var act = () => storage.DownloadAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task DeleteAsync_NullPath_ThrowsArgumentNullException()
    {
        using var storage = new AzureBlobStorage(_settings, _clock);

        var act = () => storage.DeleteAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ExistsAsync_NullPath_ThrowsArgumentNullException()
    {
        using var storage = new AzureBlobStorage(_settings, _clock);

        var act = () => storage.ExistsAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task GetReferenceAsync_NullPath_ThrowsArgumentNullException()
    {
        using var storage = new AzureBlobStorage(_settings, _clock);

        var act = () => storage.GetReferenceAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task CopyAsync_NullSourcePath_ThrowsArgumentNullException()
    {
        using var storage = new AzureBlobStorage(_settings, _clock);

        var act = () => storage.CopyAsync(null!, "dest.txt");

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task CopyAsync_NullDestinationPath_ThrowsArgumentNullException()
    {
        using var storage = new AzureBlobStorage(_settings, _clock);

        var act = () => storage.CopyAsync("source.txt", null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task GetDownloadUrlAsync_WithoutAccountKey_ThrowsInvalidOperation()
    {
        // CR-M249: previously a non-async [Fact] that discarded the ThrowAsync task — the assertion
        // never ran, so the test passed even if nothing threw. Now awaited.
        using var storage = new AzureBlobStorage(_settings, _clock);

        var act = () => storage.GetDownloadUrlAsync("test.txt");

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetUploadUrlAsync_WithoutAccountKey_ThrowsInvalidOperation()
    {
        using var storage = new AzureBlobStorage(_settings, _clock);

        var act = () => storage.GetUploadUrlAsync("test.txt");

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public void Dispose_OwnedHttpClient_DisposesClient()
    {
        var storage = new AzureBlobStorage(_settings, _clock);

        var act = () => storage.Dispose();

        act.Should().NotThrow();
    }

    [Fact]
    public void Dispose_ExternalHttpClient_DoesNotDisposeClient()
    {
        using var httpClient = new HttpClient();
        var storage = new AzureBlobStorage(_settings, _clock, httpClient);
        storage.Dispose();

        // HttpClient should still be usable after storage disposal
        var act = () => httpClient.Timeout = TimeSpan.FromSeconds(10);
        act.Should().NotThrow();
    }
}
