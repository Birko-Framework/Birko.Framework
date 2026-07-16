using System.Text;
using Birko.Storage.Local;
using FluentAssertions;
using Xunit;

namespace Birko.Storage.Tests.Local;

/// <summary>
/// CR-L370 coverage: the non-seekable MaxFileSize streaming path (CopyWithLimitAsync), partial-file
/// behavior on a failed upload, and CopyAsync metadata inheritance + overwrite-collision.
/// CR-L367: a seekable stream that under-reports Length must not bypass MaxFileSize.
/// CR-L369: MoveAsync (File.Move based) preserves source metadata and removes the source's companion meta.
/// </summary>
public class LocalFileStorageCoverageTests : IDisposable
{
    private readonly string _tempDir;
    private readonly LocalFileStorage _storage;

    public LocalFileStorageCoverageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "birko-storage-cov-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _storage = new LocalFileStorage(new StorageSettings(_tempDir, "test"), new Birko.Time.SystemDateTimeProvider());
    }

    public void Dispose()
    {
        _storage.Dispose();
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    /// <summary>Forward-only stream (CanSeek=false) so uploads take the CopyWithLimitAsync path.</summary>
    private sealed class NonSeekableStream : Stream
    {
        private readonly MemoryStream _inner;
        public NonSeekableStream(byte[] data) => _inner = new MemoryStream(data);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _inner.Position; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) _inner.Dispose(); base.Dispose(disposing); }
    }

    /// <summary>Seekable stream that lies about Length (reports fewer bytes than it actually yields).</summary>
    private sealed class LyingLengthStream : Stream
    {
        private readonly MemoryStream _inner;
        private readonly long _reportedLength;
        public LyingLengthStream(byte[] data, long reportedLength)
        {
            _inner = new MemoryStream(data);
            _reportedLength = reportedLength;
        }
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _reportedLength;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) _inner.Dispose(); base.Dispose(disposing); }
    }

    [Fact]
    public async Task UploadAsync_NonSeekable_OverLimit_ThrowsFileTooLarge()
    {
        using var stream = new NonSeekableStream(new byte[1024]);
        var options = new StorageOptions { MaxFileSize = 100 };

        var act = () => _storage.UploadAsync("nonseek-large.bin", stream, "application/octet-stream", options);

        await act.Should().ThrowAsync<FileTooLargeException>();
    }

    [Fact]
    public async Task UploadAsync_NonSeekable_UnderLimit_Succeeds()
    {
        using var stream = new NonSeekableStream(new byte[50]);
        var options = new StorageOptions { MaxFileSize = 100 };

        var reference = await _storage.UploadAsync("nonseek-ok.bin", stream, "application/octet-stream", options);

        reference.Size.Should().Be(50);
        (await _storage.ExistsAsync("nonseek-ok.bin")).Should().BeTrue();
    }

    [Fact]
    public async Task UploadAsync_OverLimit_LeavesNoPartialFile()
    {
        using var stream = new NonSeekableStream(new byte[1024]);
        var options = new StorageOptions { MaxFileSize = 100 };

        var act = () => _storage.UploadAsync("partial.bin", stream, "application/octet-stream", options);
        await act.Should().ThrowAsync<FileTooLargeException>();

        // The atomic temp-file write must leave no file at the destination and no leftover .tmp files.
        (await _storage.ExistsAsync("partial.bin")).Should().BeFalse();
        Directory.GetFiles(_tempDir).Should().BeEmpty();
    }

    [Fact]
    public async Task UploadAsync_SeekableUnderReportingLength_StillEnforcesMaxSize()
    {
        // CR-L367: Length says 10 (under the 100 limit so the up-front check passes) but the stream yields
        // 1024 bytes — the post-write re-check must catch it.
        using var stream = new LyingLengthStream(new byte[1024], reportedLength: 10);
        var options = new StorageOptions { MaxFileSize = 100 };

        var act = () => _storage.UploadAsync("liar.bin", stream, "application/octet-stream", options);

        await act.Should().ThrowAsync<FileTooLargeException>();
        (await _storage.ExistsAsync("liar.bin")).Should().BeFalse();
    }

    [Fact]
    public async Task CopyAsync_InheritsContentTypeAndMetadataFromSource()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("payload"));
        var options = new StorageOptions { Metadata = new Dictionary<string, string> { ["owner"] = "alice" } };
        await _storage.UploadAsync("src.txt", stream, "text/markdown", options);

        // No options on the copy → ContentType and Metadata come from the source's .meta.json.
        var reference = await _storage.CopyAsync("src.txt", "dst.txt");

        reference.ContentType.Should().Be("text/markdown");
        reference.Metadata.Should().ContainKey("owner").WhoseValue.Should().Be("alice");
    }

    [Fact]
    public async Task CopyAsync_OverwriteFalse_Collision_Throws()
    {
        using var s1 = new MemoryStream(new byte[] { 1 });
        await _storage.UploadAsync("a.txt", s1, "text/plain");
        using var s2 = new MemoryStream(new byte[] { 2 });
        await _storage.UploadAsync("b.txt", s2, "text/plain");

        var act = () => _storage.CopyAsync("a.txt", "b.txt", new StorageOptions { OverwriteExisting = false });

        await act.Should().ThrowAsync<FileAlreadyExistsException>();
    }

    [Fact]
    public async Task MoveAsync_PreservesMetadataAndRemovesSourceMeta()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("move payload"));
        var options = new StorageOptions { Metadata = new Dictionary<string, string> { ["k"] = "v" } };
        await _storage.UploadAsync("m-src.txt", stream, "text/csv", options);

        var reference = await _storage.MoveAsync("m-src.txt", "m-dst.txt");

        reference.ContentType.Should().Be("text/csv");
        reference.Metadata.Should().ContainKey("k").WhoseValue.Should().Be("v");
        (await _storage.ExistsAsync("m-src.txt")).Should().BeFalse();
        (await _storage.ExistsAsync("m-dst.txt")).Should().BeTrue();
        // The source's companion metadata must not be left orphaned.
        File.Exists(Path.Combine(_tempDir, "m-src.txt.meta.json")).Should().BeFalse();
        File.Exists(Path.Combine(_tempDir, "m-dst.txt.meta.json")).Should().BeTrue();
    }

    [Fact]
    public async Task MoveAsync_OntoItself_IsNoOp_AndKeepsFile()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("self"));
        await _storage.UploadAsync("self.txt", stream, "text/plain", new StorageOptions { OverwriteExisting = true });

        var reference = await _storage.MoveAsync("self.txt", "self.txt", new StorageOptions { OverwriteExisting = true });

        reference.Path.Should().Be("self.txt");
        (await _storage.ExistsAsync("self.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task MoveAsync_OverwriteFalse_Collision_Throws()
    {
        using var s1 = new MemoryStream(new byte[] { 1 });
        await _storage.UploadAsync("ms.txt", s1, "text/plain");
        using var s2 = new MemoryStream(new byte[] { 2 });
        await _storage.UploadAsync("md.txt", s2, "text/plain");

        var act = () => _storage.MoveAsync("ms.txt", "md.txt", new StorageOptions { OverwriteExisting = false });

        await act.Should().ThrowAsync<FileAlreadyExistsException>();
        // The failed move must not have consumed the source.
        (await _storage.ExistsAsync("ms.txt")).Should().BeTrue();
    }
}
