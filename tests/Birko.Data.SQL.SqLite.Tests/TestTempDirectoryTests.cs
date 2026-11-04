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
            using (var held = new FileStream(file, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            {
                var removed = TestTempDirectory.DeleteOrRecord(root);

                removed.Should().BeFalse("a file held open with no sharing cannot be deleted on Windows");
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
}
