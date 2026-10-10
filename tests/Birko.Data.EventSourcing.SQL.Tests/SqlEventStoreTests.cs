using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.EventSourcing.Events;
using Birko.Data.EventSourcing.SQL;
using Birko.Data.EventSourcing.SQL.Models;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite.Stores;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Birko.Data.EventSourcing.SQL.Tests;

/// <summary>
/// TASK-529 — <see cref="SqlEventStore{DB}"/> on SQLite (a real database file per test). Against DraCode's store it
/// replaces: an append-range is one transaction, (aggregate, version) is unique in the database so a concurrent
/// append at a taken version fails with <see cref="EventStoreConcurrencyException"/>, and the current version is
/// one MAX query.
/// </summary>
public sealed class SqlEventStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly SqLiteSettings _settings;

    public SqlEventStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "birko_events_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _settings = new SqLiteSettings(_dir, "events.db");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch { /* not the assertion */ }
    }

    // A fresh store instance each call — a second writer, as in a second process.
    private SqlEventStore<SqLiteConnector> NewStore()
    {
        var inner = new AsyncSQLiteStore<DomainEventModel>();
        inner.SetSettings(_settings);
        return new SqlEventStore<SqLiteConnector>(inner);
    }

    private static DomainEvent Event(Guid aggregate, long version, string type = "Changed")
        => new(aggregate, version, type, $"{{\"v\":{version}}}");

    private async Task<long> RawCount()
    {
        await using var c = new SqliteConnection($"Data Source={Path.Combine(_dir, "events.db")}");
        await c.OpenAsync();
        var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM \"__Events\"";
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    // ── append / read / version ──

    [Fact]
    public async Task Append_then_read_returns_the_events_in_version_order_with_every_field()
    {
        var store = NewStore();
        var a = Guid.NewGuid();
        var user = Guid.NewGuid();
        var second = Event(a, 2, "Renamed");
        second.Metadata = "{\"source\":\"test\"}";
        second.UserGuid = user;

        await store.AppendRangeAsync(new[] { second, Event(a, 1, "Created") });
        await store.AppendAsync(Event(Guid.NewGuid(), 1)); // another aggregate

        var read = (await store.ReadAsync(a)).ToList();
        read.Select(e => e.Version).Should().Equal(1, 2);
        var r2 = read[1];
        r2.EventGuid.Should().Be(second.EventGuid);
        r2.AggregateGuid.Should().Be(a);
        r2.EventType.Should().Be("Renamed");
        r2.EventData.Should().Be("{\"v\":2}");
        r2.Metadata.Should().Be("{\"source\":\"test\"}");
        r2.UserGuid.Should().Be(user);
        r2.OccurredAt.Should().BeCloseTo(second.OccurredAt, TimeSpan.FromMilliseconds(1));
        r2.OccurredAt.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public async Task Version_slices_and_the_time_tail()
    {
        var store = NewStore();
        var a = Guid.NewGuid();
        await store.AppendRangeAsync(Enumerable.Range(1, 5).Select(v => Event(a, v)));

        (await store.ReadUpToVersionAsync(a, 3)).Select(e => e.Version).Should().Equal(1, 2, 3);
        (await store.ReadFromVersionAsync(a, 4)).Select(e => e.Version).Should().Equal(4, 5);
        (await store.ReadAllFromAsync(DateTime.UtcNow.AddMinutes(-1))).Should().HaveCount(5);
        (await store.ReadAllFromAsync(DateTime.UtcNow.AddMinutes(1))).Should().BeEmpty();
    }

    [Fact]
    public async Task GetVersion_is_the_highest_version_and_0_for_an_unknown_aggregate()
    {
        var store = NewStore();
        var a = Guid.NewGuid();
        (await store.GetVersionAsync(a)).Should().Be(0);

        await store.AppendRangeAsync(new[] { Event(a, 1), Event(a, 2), Event(a, 3) });
        await store.AppendAsync(Event(Guid.NewGuid(), 9));

        (await store.GetVersionAsync(a)).Should().Be(3);
    }

    // ── concurrency ──

    [Fact]
    public async Task An_append_at_a_taken_version_throws_the_typed_conflict_and_writes_nothing()
    {
        var a = Guid.NewGuid();
        await NewStore().AppendAsync(Event(a, 1, "First"));

        var act = async () => await NewStore().AppendAsync(Event(a, 1, "Second"));

        var thrown = (await act.Should().ThrowAsync<EventStoreConcurrencyException>()).Which;
        thrown.AggregateGuid.Should().Be(a);
        thrown.Version.Should().Be(1);
        (await NewStore().ReadAsync(a)).Single().EventType.Should().Be("First");
    }

    [Fact]
    public async Task A_batch_that_conflicts_part_way_leaves_nothing_behind()
    {
        var a = Guid.NewGuid();
        await NewStore().AppendAsync(Event(a, 3));

        // 1 and 2 are free; 3 is taken — the batch must not leave 1 and 2 committed.
        var act = async () => await NewStore().AppendRangeAsync(new[] { Event(a, 1), Event(a, 2), Event(a, 3), Event(a, 4) });

        (await act.Should().ThrowAsync<EventStoreConcurrencyException>()).Which.Version.Should().Be(3);
        (await NewStore().ReadAsync(a)).Select(e => e.Version).Should().Equal(3);
        (await RawCount()).Should().Be(1);
    }

    /// <summary>
    /// The settings constructor builds the GENERIC <c>AsyncDataBaseBulkStore</c>, whose bulk create inserts one row
    /// at a time — unlike the provider stores, whose bulk insert is one transaction / statement. On that path only
    /// the event store's own unit of work makes the batch atomic, so it is exercised here directly.
    /// </summary>
    [Fact]
    public async Task A_batch_that_conflicts_part_way_leaves_nothing_behind_on_the_generic_store()
    {
        SqlEventStore<SqLiteConnector> Generic()
        {
            var inner = new Birko.Data.SQL.Stores.AsyncDataBaseBulkStore<SqLiteConnector, DomainEventModel>();
            inner.SetSettings(_settings);
            return new SqlEventStore<SqLiteConnector>(inner);
        }
        var a = Guid.NewGuid();
        await Generic().AppendAsync(Event(a, 3));

        var act = async () => await Generic().AppendRangeAsync(new[] { Event(a, 1), Event(a, 2), Event(a, 3), Event(a, 4) });

        (await act.Should().ThrowAsync<EventStoreConcurrencyException>()).Which.Version.Should().Be(3);
        (await RawCount()).Should().Be(1, "rows 1 and 2 were inserted before the conflict and must be rolled back");
    }

    [Fact]
    public async Task A_duplicate_version_inside_one_batch_is_a_conflict_too()
    {
        var a = Guid.NewGuid();

        var act = async () => await NewStore().AppendRangeAsync(new[] { Event(a, 1), Event(a, 2), Event(a, 2) });

        (await act.Should().ThrowAsync<EventStoreConcurrencyException>()).Which.Version.Should().Be(2);
        (await NewStore().GetVersionAsync(a)).Should().Be(0);
    }

    [Fact]
    public async Task Concurrent_writers_racing_for_each_version_end_with_exactly_one_event_per_version()
    {
        var a = Guid.NewGuid();
        await NewStore().AppendAsync(Event(a, 1)); // table and index exist before the race
        const int writers = 8;
        var conflicts = 0;

        // Every writer reads the version, then appends version + 1 — the read-then-write race the unique index decides.
        await Task.WhenAll(Enumerable.Range(0, writers).Select(_ => Task.Run(async () =>
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var store = NewStore();
                var next = await store.GetVersionAsync(a) + 1;
                try
                {
                    await store.AppendAsync(Event(a, next));
                }
                catch (EventStoreConcurrencyException)
                {
                    System.Threading.Interlocked.Increment(ref conflicts);
                }
                catch (Exception ex) when (ex.ToString().Contains("database is locked"))
                {
                    // SQLite serializes writers; a busy timeout is not the property under test.
                }
            }
        })));

        var versions = (await NewStore().ReadAsync(a)).Select(e => e.Version).ToList();
        versions.Should().OnlyHaveUniqueItems("the database refuses a second event at a taken version");
        versions.Should().Equal(Enumerable.Range(1, versions.Count).Select(v => (long)v), "no gaps either");
        conflicts.Should().BeGreaterThan(0, "the race must actually have been contested to prove anything");
    }

    // ── the caller's boundary ──

    [Fact]
    public async Task An_append_inside_a_callers_unit_of_work_rolls_back_with_it()
    {
        var store = NewStore();
        var a = Guid.NewGuid();
        await store.AppendAsync(Event(a, 1)); // schema outside the boundary

        await using (var uow = SqlUnitOfWork.FromStore(store.Store))
        {
            await uow.BeginAsync();
            await store.AppendAsync(Event(a, 2));
            await uow.RollbackAsync();
        }
        (await store.GetVersionAsync(a)).Should().Be(1, "the append joined the caller's transaction");

        await using (var uow = SqlUnitOfWork.FromStore(store.Store))
        {
            await uow.BeginAsync();
            await store.AppendAsync(Event(a, 2));
            await uow.CommitAsync();
        }
        (await store.GetVersionAsync(a)).Should().Be(2);
    }

    // ── the guard itself ──

    [Fact]
    public async Task The_unique_index_exists_in_the_database_after_the_first_append()
    {
        await NewStore().AppendAsync(Event(Guid.NewGuid(), 1));

        await using var c = new SqliteConnection($"Data Source={Path.Combine(_dir, "events.db")}");
        await c.OpenAsync();
        var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'index' AND name = $n";
        cmd.Parameters.AddWithValue("$n", DomainEventModel.UniqueIndexName);
        var sql = (string?)await cmd.ExecuteScalarAsync();

        sql.Should().NotBeNull();
        sql!.ToUpperInvariant().Should().Contain("UNIQUE");
    }

    [Fact]
    public async Task The_SQLite_classifier_answers_a_unique_violation_and_nothing_else()
    {
        var connector = Birko.Data.SQL.DataBase.GetConnector<SqLiteConnector>(_settings);
        await using var c = new SqliteConnection("Data Source=:memory:");
        await c.OpenAsync();
        var cmd = c.CreateCommand();
        cmd.CommandText = "CREATE TABLE t (k INTEGER UNIQUE); INSERT INTO t VALUES (1);";
        await cmd.ExecuteNonQueryAsync();

        async Task<Exception> Fail(string sql)
        {
            cmd.CommandText = sql;
            try { await cmd.ExecuteNonQueryAsync(); } catch (Exception ex) { return ex; }
            throw new InvalidOperationException("expected a failure: " + sql);
        }

        var duplicate = await Fail("INSERT INTO t VALUES (1)");
        connector.IsUniqueViolationException(duplicate).Should().BeTrue();
        connector.IsUniqueViolationException(new Exception("wrapped", duplicate)).Should().BeTrue("InitException re-wraps");
        connector.IsUniqueViolationException(await Fail("INSERT INTO missing VALUES (1)")).Should().BeFalse();
        connector.IsUniqueViolationException(await Fail("CREATE TABLE n (k INTEGER NOT NULL); INSERT INTO n VALUES (NULL)"))
            .Should().BeFalse("a NOT NULL constraint is a different constraint");
    }
}
