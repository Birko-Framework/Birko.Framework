using System.Text;
using Birko.Storage.Local;
using FluentAssertions;
using Xunit;

namespace Birko.Storage.Tests.Local;

public class LocalFileStorageTests : IDisposable
{
    private readonly string _tempDir;
    private readonly LocalFileStorage _storage;

    public LocalFileStorageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "birko-storage-tests-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _storage = new LocalFileStorage(new StorageSettings(_tempDir, "test"));
    }

    public void Dispose()
    {
        _storage.Dispose();
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    #region Upload

    [Fact]
    public async Task UploadAsync_WritesFileAndReturnsReference()
    {
        var data = Encoding.UTF8.GetBytes("hello world");
        using var stream = new MemoryStream(data);

        var reference = await _storage.UploadAsync("test.txt", stream, "text/plain");

        reference.Path.Should().Be("test.txt");
        reference.FileName.Should().Be("test.txt");
        reference.ContentType.Should().Be("text/plain");
        reference.Size.Should().Be(data.Length);
        reference.ETag.Should().NotBeNullOrEmpty();
        reference.Metadata.Should().NotBeNull();
        File.Exists(Path.Combine(_tempDir, "test.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task UploadAsync_CreatesDirectoriesAutomatically()
    {
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });

        await _storage.UploadAsync("deep/nested/dir/file.bin", stream, "application/octet-stream");

        File.Exists(Path.Combine(_tempDir, "deep", "nested", "dir", "file.bin")).Should().BeTrue();
    }

    [Fact]
    public async Task UploadAsync_ExistingFile_ThrowsFileAlreadyExistsException()
    {
        using var stream1 = new MemoryStream(new byte[] { 1 });
        await _storage.UploadAsync("dup.txt", stream1, "text/plain");

        using var stream2 = new MemoryStream(new byte[] { 2 });
        var act = () => _storage.UploadAsync("dup.txt", stream2, "text/plain");

        await act.Should().ThrowAsync<FileAlreadyExistsException>();
    }

    [Fact]
    public async Task UploadAsync_OverwriteTrue_Succeeds()
    {
        using var stream1 = new MemoryStream(new byte[] { 1 });
        await _storage.UploadAsync("overwrite.txt", stream1, "text/plain");

        var newData = new byte[] { 2, 3, 4 };
        using var stream2 = new MemoryStream(newData);
        var options = new StorageOptions { OverwriteExisting = true };

        var reference = await _storage.UploadAsync("overwrite.txt", stream2, "text/plain", options);

        reference.Size.Should().Be(newData.Length);
    }

    [Fact]
    public async Task UploadAsync_ExceedsMaxSize_ThrowsFileTooLargeException()
    {
        using var stream = new MemoryStream(new byte[1024]);
        var options = new StorageOptions { MaxFileSize = 100 };

        var act = () => _storage.UploadAsync("large.bin", stream, "application/octet-stream", options);

        await act.Should().ThrowAsync<FileTooLargeException>();
    }

    [Fact]
    public async Task UploadAsync_DisallowedContentType_ThrowsContentTypeNotAllowedException()
    {
        using var stream = new MemoryStream(new byte[] { 1 });
        var options = new StorageOptions { AllowedContentTypes = new[] { "image/jpeg" } };

        var act = () => _storage.UploadAsync("file.txt", stream, "text/plain", options);

        await act.Should().ThrowAsync<ContentTypeNotAllowedException>();
    }

    [Fact]
    public async Task UploadAsync_StoresMetadata()
    {
        using var stream = new MemoryStream(new byte[] { 1 });
        var options = new StorageOptions { Metadata = new Dictionary<string, string> { ["author"] = "test" } };

        await _storage.UploadAsync("meta.txt", stream, "text/plain", options);

        var result = await _storage.GetReferenceAsync("meta.txt");
        result.Found.Should().BeTrue();
        result.Value!.Metadata.Should().ContainKey("author").WhoseValue.Should().Be("test");
    }

    #endregion

    #region Download

    [Fact]
    public async Task DownloadAsync_ExistingFile_ReturnsStream()
    {
        var data = Encoding.UTF8.GetBytes("content");
        using var uploadStream = new MemoryStream(data);
        await _storage.UploadAsync("download.txt", uploadStream, "text/plain");

        var result = await _storage.DownloadAsync("download.txt");

        result.Found.Should().BeTrue();
        using var reader = new StreamReader(result.Value!);
        var content = await reader.ReadToEndAsync();
        content.Should().Be("content");
    }

    [Fact]
    public async Task DownloadAsync_NonExistentFile_ReturnsNotFound()
    {
        var result = await _storage.DownloadAsync("nonexistent.txt");

        result.Found.Should().BeFalse();
    }

    #endregion

    #region Delete

    [Fact]
    public async Task DeleteAsync_ExistingFile_ReturnsTrue()
    {
        using var stream = new MemoryStream(new byte[] { 1 });
        await _storage.UploadAsync("delete-me.txt", stream, "text/plain");

        var deleted = await _storage.DeleteAsync("delete-me.txt");

        deleted.Should().BeTrue();
        File.Exists(Path.Combine(_tempDir, "delete-me.txt")).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_NonExistentFile_ReturnsFalse()
    {
        var deleted = await _storage.DeleteAsync("nope.txt");

        deleted.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_RemovesMetadataFile()
    {
        using var stream = new MemoryStream(new byte[] { 1 });
        await _storage.UploadAsync("with-meta.txt", stream, "text/plain");

        await _storage.DeleteAsync("with-meta.txt");

        File.Exists(Path.Combine(_tempDir, "with-meta.txt.meta.json")).Should().BeFalse();
    }

    #endregion

    #region Exists

    [Fact]
    public async Task ExistsAsync_ExistingFile_ReturnsTrue()
    {
        using var stream = new MemoryStream(new byte[] { 1 });
        await _storage.UploadAsync("exists.txt", stream, "text/plain");

        var exists = await _storage.ExistsAsync("exists.txt");

        exists.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_NonExistentFile_ReturnsFalse()
    {
        var exists = await _storage.ExistsAsync("nope.txt");

        exists.Should().BeFalse();
    }

    #endregion

    #region GetReference

    [Fact]
    public async Task GetReferenceAsync_ExistingFile_ReturnsMetadata()
    {
        var data = Encoding.UTF8.GetBytes("reference test");
        using var stream = new MemoryStream(data);
        await _storage.UploadAsync("ref.txt", stream, "text/plain");

        var result = await _storage.GetReferenceAsync("ref.txt");

        result.Found.Should().BeTrue();
        result.Value!.Path.Should().Be("ref.txt");
        result.Value!.ContentType.Should().Be("text/plain");
        result.Value!.Size.Should().Be(data.Length);
        result.Value!.ETag.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetReferenceAsync_NonExistentFile_ReturnsNotFound()
    {
        var result = await _storage.GetReferenceAsync("nope.txt");

        result.Found.Should().BeFalse();
    }

    #endregion

    #region List

    [Fact]
    public async Task ListAsync_ReturnsAllFiles()
    {
        using var s1 = new MemoryStream(new byte[] { 1 });
        using var s2 = new MemoryStream(new byte[] { 2 });
        await _storage.UploadAsync("a.txt", s1, "text/plain");
        await _storage.UploadAsync("b.txt", s2, "text/plain");

        var files = await _storage.ListAsync();

        files.Should().HaveCountGreaterOrEqualTo(2);
        files.Select(f => f.FileName).Should().Contain("a.txt").And.Contain("b.txt");
    }

    [Fact]
    public async Task ListAsync_WithPrefix_FiltersResults()
    {
        using var s1 = new MemoryStream(new byte[] { 1 });
        using var s2 = new MemoryStream(new byte[] { 2 });
        await _storage.UploadAsync("images/photo.jpg", s1, "image/jpeg");
        await _storage.UploadAsync("docs/readme.txt", s2, "text/plain");

        var files = await _storage.ListAsync(prefix: "images/");

        files.Should().ContainSingle();
        files[0].FileName.Should().Be("photo.jpg");
    }

    [Fact]
    public async Task ListAsync_WithMaxResults_LimitsOutput()
    {
        for (int i = 0; i < 5; i++)
        {
            using var s = new MemoryStream(new byte[] { (byte)i });
            await _storage.UploadAsync($"limited/{i}.txt", s, "text/plain");
        }

        var files = await _storage.ListAsync(prefix: "limited/", maxResults: 2);

        files.Should().HaveCount(2);
    }

    #endregion

    #region Copy / Move

    [Fact]
    public async Task CopyAsync_CreatesNewFile()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("copy me"));
        await _storage.UploadAsync("original.txt", stream, "text/plain");

        var reference = await _storage.CopyAsync("original.txt", "copied.txt");

        reference.Path.Should().Be("copied.txt");
        (await _storage.ExistsAsync("original.txt")).Should().BeTrue();
        (await _storage.ExistsAsync("copied.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task MoveAsync_RemovesSource()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("move me"));
        await _storage.UploadAsync("source.txt", stream, "text/plain");

        var reference = await _storage.MoveAsync("source.txt", "destination.txt");

        reference.Path.Should().Be("destination.txt");
        (await _storage.ExistsAsync("source.txt")).Should().BeFalse();
        (await _storage.ExistsAsync("destination.txt")).Should().BeTrue();
    }

    #endregion

    #region Path Validation

    [Fact]
    public async Task PathValidation_RejectsTraversal()
    {
        using var stream = new MemoryStream(new byte[] { 1 });

        var act = () => _storage.UploadAsync("../escape.txt", stream, "text/plain");

        await act.Should().ThrowAsync<InvalidPathException>();
    }

    [Fact]
    public async Task PathValidation_RejectsAbsolutePath()
    {
        using var stream = new MemoryStream(new byte[] { 1 });

        var act = () => _storage.UploadAsync("/etc/passwd", stream, "text/plain");

        await act.Should().ThrowAsync<InvalidPathException>();
    }

    [Fact]
    public async Task PathValidation_NormalizesSlashes()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("normalized"));
        await _storage.UploadAsync("folder/file.txt", stream, "text/plain");

        var exists = await _storage.ExistsAsync("folder/file.txt");
        exists.Should().BeTrue();
    }

    #endregion

    #region PathPrefix

    [Fact]
    public async Task PathPrefix_IsAppliedToAllOperations()
    {
        var prefixedStorage = new LocalFileStorage(new StorageSettings(_tempDir, "test", "tenant-1"));

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("tenant data"));
        await prefixedStorage.UploadAsync("data.txt", stream, "text/plain");

        File.Exists(Path.Combine(_tempDir, "tenant-1", "data.txt")).Should().BeTrue();
        (await prefixedStorage.ExistsAsync("data.txt")).Should().BeTrue();

        prefixedStorage.Dispose();
    }

    #endregion
}
