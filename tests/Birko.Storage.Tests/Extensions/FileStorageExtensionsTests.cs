using System.Text;
using Birko.Storage.Local;
using FluentAssertions;
using Xunit;

namespace Birko.Storage.Tests.Extensions;

public class FileStorageExtensionsTests : IDisposable
{
    private readonly string _tempDir;
    private readonly LocalFileStorage _storage;

    public FileStorageExtensionsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "birko-storage-ext-tests-" + Guid.NewGuid().ToString("N")[..8]);
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

    [Fact]
    public async Task UploadBytesAsync_UploadsAndReturnsReference()
    {
        var data = Encoding.UTF8.GetBytes("byte upload");

        var reference = await _storage.UploadBytesAsync("bytes.txt", data, "text/plain");

        reference.Size.Should().Be(data.Length);
        (await _storage.ExistsAsync("bytes.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task DownloadBytesAsync_ReturnsCorrectBytes()
    {
        var data = Encoding.UTF8.GetBytes("download bytes");
        await _storage.UploadBytesAsync("download-bytes.txt", data, "text/plain");

        var result = await _storage.DownloadBytesAsync("download-bytes.txt");

        result.Found.Should().BeTrue();
        Encoding.UTF8.GetString(result.Value!).Should().Be("download bytes");
    }

    [Fact]
    public async Task DownloadBytesAsync_NotFound_ReturnsNotFound()
    {
        var result = await _storage.DownloadBytesAsync("nope.txt");

        result.Found.Should().BeFalse();
    }

    [Fact]
    public async Task DownloadToFileAsync_WritesLocalFile()
    {
        var data = Encoding.UTF8.GetBytes("to file");
        await _storage.UploadBytesAsync("source.txt", data, "text/plain");

        var localPath = Path.Combine(_tempDir, "downloaded", "output.txt");
        var downloaded = await _storage.DownloadToFileAsync("source.txt", localPath);

        downloaded.Should().BeTrue();
        File.Exists(localPath).Should().BeTrue();
        (await File.ReadAllTextAsync(localPath)).Should().Be("to file");
    }

    [Fact]
    public async Task DownloadToFileAsync_NotFound_ReturnsFalse()
    {
        var localPath = Path.Combine(_tempDir, "no-output.txt");
        var downloaded = await _storage.DownloadToFileAsync("nope.txt", localPath);

        downloaded.Should().BeFalse();
        File.Exists(localPath).Should().BeFalse();
    }
}
