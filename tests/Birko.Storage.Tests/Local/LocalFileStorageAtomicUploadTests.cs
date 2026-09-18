using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Birko.Storage;
using Birko.Storage.Local;
using FluentAssertions;
using Xunit;

namespace Birko.Storage.Tests.Local;

/// <summary>
/// CR-H141: a write that fails or is cancelled mid-stream must not leave an orphaned/partial file,
/// and must not destroy an existing good file (the old code opened the destination with
/// FileMode.Create up front and streamed into it directly).
/// </summary>
public class LocalFileStorageAtomicUploadTests : IDisposable
{
    private readonly string _tempDir;
    private readonly LocalFileStorage _storage;

    public LocalFileStorageAtomicUploadTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "birko-storage-atomic-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _storage = new LocalFileStorage(new StorageSettings(_tempDir, "test"), new Birko.Time.SystemDateTimeProvider());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    /// <summary>A non-seekable stream that yields some bytes then throws, simulating a mid-write failure.</summary>
    private sealed class ThrowingStream : Stream
    {
        private readonly int _bytesBeforeThrow;
        private int _emitted;

        public ThrowingStream(int bytesBeforeThrow) => _bytesBeforeThrow = bytesBeforeThrow;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_emitted >= _bytesBeforeThrow)
            {
                throw new IOException("simulated mid-stream failure");
            }

            var n = Math.Min(count, _bytesBeforeThrow - _emitted);
            for (int i = 0; i < n; i++) buffer[offset + i] = 0x42;
            _emitted += n;
            return n;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => Task.FromResult(Read(buffer, offset, count));
    }

    [Fact]
    public async Task UploadAsync_FailsMidStream_LeavesNoOrphanFile()
    {
        using var content = new ThrowingStream(bytesBeforeThrow: 16);

        var act = () => _storage.UploadAsync("fail.bin", content, "application/octet-stream");

        await act.Should().ThrowAsync<IOException>();
        File.Exists(Path.Combine(_tempDir, "fail.bin")).Should().BeFalse("no partial file must remain");
        Directory.GetFiles(_tempDir, "*.tmp").Should().BeEmpty("the temp file must be cleaned up");
    }

    [Fact]
    public async Task UploadAsync_FailedOverwrite_PreservesExistingFile()
    {
        await _storage.UploadAsync("keep.txt", new MemoryStream(Encoding.UTF8.GetBytes("GOOD")), "text/plain");

        using var bad = new ThrowingStream(bytesBeforeThrow: 4);
        var act = () => _storage.UploadAsync("keep.txt", bad, "text/plain", new StorageOptions { OverwriteExisting = true });

        await act.Should().ThrowAsync<IOException>();
        File.ReadAllText(Path.Combine(_tempDir, "keep.txt")).Should().Be("GOOD", "the previous good file must survive a failed overwrite");
    }

    [Fact]
    public async Task UploadAsync_CancelledBeforeWrite_LeavesNoOrphanFile()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        using var content = new MemoryStream(new byte[] { 1, 2, 3, 4 });

        var act = () => _storage.UploadAsync("cancelled.bin", content, "application/octet-stream", ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        File.Exists(Path.Combine(_tempDir, "cancelled.bin")).Should().BeFalse();
        Directory.GetFiles(_tempDir, "*.tmp").Should().BeEmpty();
    }
}
