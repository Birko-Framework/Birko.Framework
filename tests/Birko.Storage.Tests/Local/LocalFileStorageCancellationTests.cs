using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Birko.Storage.Local;
using FluentAssertions;
using Xunit;

namespace Birko.Storage.Tests.Local;

/// <summary>
/// CR-M246: DownloadAsync/DeleteAsync/ExistsAsync/ListAsync accepted a CancellationToken but never
/// observed it. They now call ThrowIfCancellationRequested() on entry (ListAsync also inside the
/// enumeration), so a pre-cancelled token throws OperationCanceledException.
/// </summary>
public class LocalFileStorageCancellationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly LocalFileStorage _storage;

    public LocalFileStorageCancellationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "birko-storage-cancel-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _storage = new LocalFileStorage(new StorageSettings(_tempDir, "test"), new Birko.Time.SystemDateTimeProvider());
    }

    public void Dispose()
    {
        _storage.Dispose();
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
    }

    private static CancellationToken Cancelled()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();
        return cts.Token;
    }

    [Fact]
    public async Task DownloadAsync_CancelledToken_Throws()
        => await _storage.Invoking(s => s.DownloadAsync("a.txt", Cancelled())).Should().ThrowAsync<OperationCanceledException>();

    [Fact]
    public async Task DeleteAsync_CancelledToken_Throws()
        => await _storage.Invoking(s => s.DeleteAsync("a.txt", Cancelled())).Should().ThrowAsync<OperationCanceledException>();

    [Fact]
    public async Task ExistsAsync_CancelledToken_Throws()
        => await _storage.Invoking(s => s.ExistsAsync("a.txt", Cancelled())).Should().ThrowAsync<OperationCanceledException>();

    [Fact]
    public async Task ListAsync_CancelledToken_Throws()
        => await _storage.Invoking(s => s.ListAsync(null, null, Cancelled())).Should().ThrowAsync<OperationCanceledException>();
}
