using System;
using System.IO;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// TASK-302: the teardown helper removes a directory a pooled SQLite connection still holds, and RECORDS one it cannot
/// remove instead of swallowing the failure.
/// </summary>
public sealed class TestTempDirectoryTests
{
    [Fact]
    public void A_directory_whose_database_is_still_pooled_is_removed()
    {
        var root = TestTempDirectory.Create("birko-ttd-pooled");
        var cs = $"Data Source={Path.Combine(root, "held.db")};Default Timeout=30";
        using (var connection = new SqliteConnection(cs))
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode=WAL; CREATE TABLE t (x INTEGER); INSERT INTO t VALUES (1);";
            cmd.ExecuteNonQuery();
        }

        // Closed, but returned to the pool: the file is still open, which is what leaked 449 directories a run.
        TestTempDirectory.Delete(root);

        Directory.Exists(root).Should().BeFalse("the helper clears this directory's pools before deleting it");
    }

    [Fact]
    public void A_directory_that_cannot_be_removed_is_recorded_not_swallowed()
    {
        var root = TestTempDirectory.Create("birko-ttd-stuck");
        var file = Path.Combine(root, "locked.bin");
        try
        {
            using (var held = Unremovable(root, file))
            {
                var removed = TestTempDirectory.DeleteOrRecord(root);

                removed.Should().BeFalse("the directory was made unremovable");
                TestTempDirectory.RecordedFailures.Should().Contain(root);
                File.ReadAllText(TestTempDirectory.FailureLogPath).Should().Contain(root);
            }
        }
        finally
        {
            TestTempDirectory.Delete(root);
        }

        Directory.Exists(root).Should().BeFalse("once the handle is released the same helper removes it");
    }

    /// <summary>
    /// Makes <paramref name="root"/> impossible to remove until disposed. On Windows a file held open with no sharing
    /// does it; on Linux it does NOT (an open file can be unlinked: this test failed every CI run from TASK-302 until
    /// TASK-820), so there the directory is made read-only, which stops a non-root user deleting its entries.
    /// </summary>
    private static IDisposable Unremovable(string root, string file)
    {
        var held = new FileStream(file, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        if (OperatingSystem.IsWindows()) return held;

        held.Dispose();
        var mode = File.GetUnixFileMode(root);
        File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        var restore = new Restore(() => File.SetUnixFileMode(root, mode));
        var probe = Path.Combine(root, "probe");
        try { File.WriteAllText(probe, ""); }
        catch (UnauthorizedAccessException) { return restore; }

        restore.Dispose();
        throw new InvalidOperationException(
            "A read-only directory did not stop a write: running as root? This test needs a non-root user on Linux.");
    }

    private sealed class Restore(Action undo) : IDisposable
    {
        public void Dispose() => undo();
    }
}
