using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite.Stores;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// TASK-305, answering TASK-258's first criterion now that a default policy exists: SQLite's bulk write, on its own
/// connection, retries a busy database <b>and converges</b> — every row once, none twice, none missing.
/// </summary>
/// <remarks>
/// <para>
/// Another connection holds the write lock (rollback journal: WAL would let the writer queue differently) for longer
/// than one attempt's busy timeout (<c>CommandTimeout = 1</c>), then releases it. The first attempt fails with
/// SQLITE_BUSY at <c>BEGIN IMMEDIATE</c>; the default policy's retry gets the lock. So success itself shows the retry,
/// and the control below — the same lock with <c>RetryPolicy.None</c> — shows that without it the write fails.
/// </para>
/// </remarks>
public class BusyRetryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"birko-busy305-{Guid.NewGuid():N}");
    private static int _seq;

    public BusyRetryTests() => Directory.CreateDirectory(_root);

    public void Dispose() => TestTempDirectory.Delete(_root);

    [Table("BusyRows")]
    public class BusyRow : AbstractDatabaseModel
    {
        public string? Value { get; set; }
    }

    private SqLiteSettings NewSettings() => new(_root, $"busy{Interlocked.Increment(ref _seq)}.db")
    {
        JournalMode = "DELETE",
        CommandTimeout = 1,
    };

    /// <summary>Takes the write lock now and releases it after <paramref name="holdMs"/>.</summary>
    private static async Task<Task> HoldWriteLock(SqLiteSettings settings, int holdMs)
    {
        var blocker = new SqliteConnection(settings.GetConnectionString());
        await blocker.OpenAsync();
        var hold = blocker.BeginTransaction(); // BEGIN IMMEDIATE: the write lock, at once
        using (var cmd = blocker.CreateCommand())
        {
            cmd.Transaction = hold;
            cmd.CommandText = "CREATE TABLE Blocker (Id INTEGER)";
            cmd.ExecuteNonQuery();
        }
        return Task.Run(async () =>
        {
            await Task.Delay(holdMs);
            hold.Rollback();
            hold.Dispose();
            blocker.Dispose();
        });
    }

    private static List<BusyRow> Rows(int n) =>
        Enumerable.Range(0, n).Select(i => new BusyRow { Guid = Guid.NewGuid(), Value = $"row{i}" }).ToList();

    private static void ShouldHoldExactly(IEnumerable<BusyRow> stored, IEnumerable<BusyRow> written)
    {
        var storedGuids = stored.Select(r => r.Guid).ToList();
        storedGuids.Should().OnlyHaveUniqueItems("a retry must not insert any row twice");
        storedGuids.Should().BeEquivalentTo(written.Select(r => r.Guid), "every row once, none missing");
    }

    [Fact]
    public async Task Async_bulk_insert_retries_a_busy_database_by_default_and_converges()
    {
        var settings = NewSettings();
        var store = new AsyncSQLiteStore<BusyRow>();
        store.SetSettings(settings);
        await store.CreateAsync(new BusyRow { Guid = Guid.NewGuid(), Value = "seed" });
        await store.DeleteAsync(x => true);

        var release = await HoldWriteLock(settings, holdMs: 1300);
        var rows = Rows(25);
        await store.CreateAsync(rows);
        await release;

        ShouldHoldExactly(await store.ReadAsync(x => true), rows);
    }

    [Fact]
    public async Task Sync_bulk_insert_retries_a_busy_database_by_default_and_converges()
    {
        var settings = NewSettings();
        var store = new SQLiteStore<BusyRow>();
        store.SetSettings(settings);
        store.Create(new BusyRow { Guid = Guid.NewGuid(), Value = "seed" });
        store.Delete(x => true);

        var release = await HoldWriteLock(settings, holdMs: 1300);
        var rows = Rows(25);
        store.Create(rows);
        await release;

        ShouldHoldExactly(store.Read(x => true), rows);
    }

    /// <summary>
    /// The connector's own bulk path (CR-M144, <c>RunBulk</c> on a connection it owns), called directly rather than
    /// through a store's boundary: it retries too, and converges.
    /// </summary>
    [Fact]
    public async Task The_connectors_own_bulk_insert_retries_a_busy_database_and_converges()
    {
        var settings = NewSettings();
        var store = new AsyncSQLiteStore<BusyRow>();
        store.SetSettings(settings);
        await store.CreateAsync(new BusyRow { Guid = Guid.NewGuid(), Value = "seed" });
        await store.DeleteAsync(x => true);
        var connector = (SqLiteConnector)DataBase.GetConnector<SqLiteConnector>(settings);

        var release = await HoldWriteLock(settings, holdMs: 1300);
        var rows = Rows(25);
        await connector.BulkInsertAsync(typeof(BusyRow), rows);
        await release;

        ShouldHoldExactly(await store.ReadAsync(x => true), rows);
    }

    [Fact]
    public async Task Control_without_the_retry_the_same_lock_fails_the_write_and_stores_nothing()
    {
        var settings = NewSettings();
        var store = new AsyncSQLiteStore<BusyRow>();
        store.SetSettings(settings);
        await store.CreateAsync(new BusyRow { Guid = Guid.NewGuid(), Value = "seed" });
        await store.DeleteAsync(x => true);
        DataBase.GetConnector<SqLiteConnector>(settings).RetryPolicy = RetryPolicy.None;

        var release = await HoldWriteLock(settings, holdMs: 1300);
        Func<Task> act = () => store.CreateAsync(Rows(25));
        (await act.Should().ThrowAsync<SqliteException>()).Which.SqliteErrorCode.Should().Be(5);
        await release;

        (await store.ReadAsync(x => true)).Should().BeEmpty("the busy attempt applied nothing");
    }
}
