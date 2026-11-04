using System;
using System.IO;
using System.Threading.Tasks;
using Birko.Helpers;
using FluentAssertions;
using Xunit;

namespace Birko.Helpers.Tests;

/// <summary>TASK-721: the one temp-then-move write the JSON and XML stores share.</summary>
public class AtomicFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "birko-atomic-" + Guid.NewGuid().ToString("N"));

    public AtomicFileTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        TestTempDirectory.Delete(_dir);
    }

    private string File1 => Path.Combine(_dir, "data.json");

    private static void Text(Stream s, string text)
    {
        using var w = new StreamWriter(s, leaveOpen: true);
        w.Write(text);
    }

    [Fact]
    public void Write_replaces_the_file_and_leaves_nothing_else()
    {
        File.WriteAllText(File1, "old, and longer than the new content");

        AtomicFile.Write(File1, s => Text(s, "new"));

        File.ReadAllText(File1).Should().Be("new");
        Directory.GetFileSystemEntries(_dir).Should().Equal(File1);
    }

    [Fact]
    public void A_failed_write_keeps_the_previous_file_and_removes_the_temp_file()
    {
        File.WriteAllText(File1, "old");

        var write = () => AtomicFile.Write(File1, s => { Text(s, "half"); throw new InvalidOperationException("boom"); });

        write.Should().Throw<InvalidOperationException>();
        File.ReadAllText(File1).Should().Be("old");
        Directory.GetFileSystemEntries(_dir).Should().Equal(File1);
    }

    [Fact]
    public async Task A_failed_async_write_keeps_the_previous_file_and_removes_the_temp_file()
    {
        File.WriteAllText(File1, "old");

        var write = () => AtomicFile.WriteAsync(File1, async s => { await Task.Yield(); throw new InvalidOperationException("boom"); });

        await write.Should().ThrowAsync<InvalidOperationException>();
        File.ReadAllText(File1).Should().Be("old");
        Directory.GetFileSystemEntries(_dir).Should().Equal(File1);
    }

    [Fact]
    public async Task WriteAsync_creates_a_new_file()
    {
        await AtomicFile.WriteAsync(File1, async s => { await s.WriteAsync(new byte[] { 65 }); });

        File.ReadAllText(File1).Should().Be("A");
    }

    [Fact]
    public void A_cancelled_async_write_keeps_the_previous_file()
    {
        File.WriteAllText(File1, "old");
        using var cts = new System.Threading.CancellationTokenSource();
        cts.Cancel();

        var write = () => AtomicFile.WriteAsync(File1, s => { Text(s, "new"); return Task.CompletedTask; }, cts.Token);

        write.Should().ThrowAsync<OperationCanceledException>().GetAwaiter().GetResult();
        File.ReadAllText(File1).Should().Be("old");
    }

    [Fact]
    public void DeleteDirectoryIfEmpty_deletes_only_an_empty_directory()
    {
        var empty = Path.Combine(_dir, "empty");
        var full = Path.Combine(_dir, "full");
        Directory.CreateDirectory(empty);
        Directory.CreateDirectory(full);
        File.WriteAllText(Path.Combine(full, "keep.txt"), "x");

        AtomicFile.DeleteDirectoryIfEmpty(empty).Should().BeTrue();
        AtomicFile.DeleteDirectoryIfEmpty(full).Should().BeFalse();
        AtomicFile.DeleteDirectoryIfEmpty(Path.Combine(_dir, "missing")).Should().BeFalse();
        AtomicFile.DeleteDirectoryIfEmpty(null).Should().BeFalse();

        Directory.Exists(empty).Should().BeFalse();
        File.Exists(Path.Combine(full, "keep.txt")).Should().BeTrue();
    }
}
