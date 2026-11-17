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
/// TASK-305, answering TASK-258's first criterion: SQLite's bulk write, on its own connection, waits out a busy
/// database <b>and converges</b> — every row once, none twice, none missing. TASK-938 bounds that wait to ONE command
/// timeout (see the tests at the end).
/// </summary>
/// <remarks>
/// <para>
/// Another connection holds the write lock (rollback journal: WAL would let the writer queue differently), then
/// releases it. Until TASK-938 the lock was held for LONGER than the busy timeout (<c>CommandTimeout = 1</c>), the
/// first attempt failed with SQLITE_BUSY at <c>BEGIN IMMEDIATE</c>, and the framework's retry got the lock. That retry
/// repeated the whole busy-wait (90 s at the 30 s default, measured by Symbio), so it is gone: the converging tests
/// now hold the lock for less than the timeout, and Microsoft.Data.Sqlite's own busy-wait gets it. The control keeps
/// the lock past the timeout with <c>RetryPolicy.None</c> and still fails.
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
    public async Task Async_bulk_insert_waits_out_a_lock_shorter_than_the_timeout_and_converges()
    {
        var settings = NewSettings();
        var store = new AsyncSQLiteStore<BusyRow>();
        store.SetSettings(settings);
        await store.CreateAsync(new BusyRow { Guid = Guid.NewGuid(), Value = "seed" });
        await store.DeleteAsync(x => true);

        var release = await HoldWriteLock(settings, holdMs: 600);
        var rows = Rows(25);
        await store.CreateAsync(rows);
        await release;

        ShouldHoldExactly(await store.ReadAsync(x => true), rows);
    }

    [Fact]
    public async Task Sync_bulk_insert_waits_out_a_lock_shorter_than_the_timeout_and_converges()
    {
        var settings = NewSettings();
        var store = new SQLiteStore<BusyRow>();
        store.SetSettings(settings);
        store.Create(new BusyRow { Guid = Guid.NewGuid(), Value = "seed" });
        store.Delete(x => true);

        var release = await HoldWriteLock(settings, holdMs: 600);
        var rows = Rows(25);
        store.Create(rows);
        await release;

        ShouldHoldExactly(store.Read(x => true), rows);
    }

    /// <summary>
    /// The connector's own bulk path (CR-M144, <c>RunBulk</c> on a connection it owns), called directly rather than
    /// through a store's boundary: it waits too, and converges.
    /// </summary>
    [Fact]
    public async Task The_connectors_own_bulk_insert_waits_out_a_short_lock_and_converges()
    {
        var settings = NewSettings();
        var store = new AsyncSQLiteStore<BusyRow>();
        store.SetSettings(settings);
        await store.CreateAsync(new BusyRow { Guid = Guid.NewGuid(), Value = "seed" });
        await store.DeleteAsync(x => true);
        var connector = (SqLiteConnector)DataBase.GetConnector<SqLiteConnector>(settings);

        var release = await HoldWriteLock(settings, holdMs: 600);
        var rows = Rows(25);
        await connector.BulkInsertAsync(typeof(BusyRow), rows);
        await release;

        ShouldHoldExactly(await store.ReadAsync(x => true), rows);
    }

    [Fact]
    public async Task Control_without_the_retry_a_lock_past_the_timeout_fails_the_write_and_stores_nothing()
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
    // ── TASK-938 (the user's decision, 2026-11-17): one busy-wait, not three ────────────────────────────────────────
    // Microsoft.Data.Sqlite already waits on a locked database until the command timeout before it reports SQLITE_BUSY,
    // so retrying that error repeated the whole wait: Symbio measured a write failing after 90 432 ms (3 x 30 s). A busy
    // error that arrives after the wait ran out is no longer retried; the write fails after ONE timeout. A busy error
    // that arrives early (no wait happened) is still retried. The tests above (TASK-305) now hold the lock for less
    // than the timeout, so the busy-wait itself gets the lock.

    [Fact]
    public async Task A_lock_held_past_the_timeout_fails_after_one_wait_not_three()
    {
        var settings = NewSettings(); // CommandTimeout = 1 s
        var store = new AsyncSQLiteStore<BusyRow>();
        store.SetSettings(settings);
        await store.CreateAsync(new BusyRow { Guid = Guid.NewGuid(), Value = "seed" });
        await store.DeleteAsync(x => true);

        var release = await HoldWriteLock(settings, holdMs: 4000);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Func<Task> act = () => store.CreateAsync(Rows(25));
        (await act.Should().ThrowAsync<SqliteException>()).Which.SqliteErrorCode.Should().Be(5);
        clock.Stop();
        await release;

        clock.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(1900),
            "one 1 s busy-wait, not (MaxRetries + 1) x 1 s; measured {0} ms", clock.ElapsedMilliseconds);
        (await store.ReadAsync(x => true)).Should().BeEmpty("the busy attempt applied nothing");
    }

    [Fact]
    public void Sync_a_lock_held_past_the_timeout_fails_after_one_wait_not_three()
    {
        var settings = NewSettings();
        var store = new SQLiteStore<BusyRow>();
        store.SetSettings(settings);
        store.Create(new BusyRow { Guid = Guid.NewGuid(), Value = "seed" });
        store.Delete(x => true);

        var release = HoldWriteLock(settings, holdMs: 4000).GetAwaiter().GetResult();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Action act = () => store.Create(Rows(25));
        act.Should().Throw<SqliteException>().Which.SqliteErrorCode.Should().Be(5);
        clock.Stop();
        release.GetAwaiter().GetResult();

        clock.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(1900),
            "one 1 s busy-wait, not (MaxRetries + 1) x 1 s; measured {0} ms", clock.ElapsedMilliseconds);
    }

    [Theory]
    [InlineData(5, 0, true)]      // BUSY, no wait happened: retried
    [InlineData(6, 0, true)]      // LOCKED, no wait happened: retried
    [InlineData(5, 1000, false)]  // BUSY after the full 1 s wait: not retried
    [InlineData(6, 1500, false)]
    public void Only_a_busy_error_that_came_before_the_wait_ran_out_is_retried(int code, int elapsedMs, bool retried)
    {
        var connector = (SqLiteConnector)DataBase.GetConnector<SqLiteConnector>(NewSettings());
        var ex = new SqliteException("locked", code);
        connector.WaitedOutLockTimeout(ex, TimeSpan.FromMilliseconds(elapsedMs)).Should().Be(!retried);
    }

    [Fact]
    public void A_zero_timeout_never_counts_as_waited_out()
    {
        // DefaultTimeout 0 means Microsoft.Data.Sqlite waits forever, so a busy error can only be an early one.
        var settings = NewSettings();
        settings.CommandTimeout = 0;
        var connector = (SqLiteConnector)DataBase.GetConnector<SqLiteConnector>(settings);
        connector.WaitedOutLockTimeout(new SqliteException("locked", 5), TimeSpan.FromSeconds(60)).Should().BeFalse();
    }
}
