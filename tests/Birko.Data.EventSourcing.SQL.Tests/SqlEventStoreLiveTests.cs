using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Configuration;
using Birko.Data.EventSourcing.Events;
using Birko.Data.EventSourcing.SQL;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Stores;
using Birko.Data.SQL.MSSql.Stores;
using Birko.Data.SQL.MySQL.Stores;
using Birko.Data.SQL.PostgreSQL.Stores;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.EventSourcing.SQL.Tests;

/// <summary>
/// TASK-529 — <see cref="SqlEventStore{DB}"/> on the live servers (Docker, per <c>.github/workflows/live-tests.yml</c>),
/// through the <c>SqlSettings</c> constructor: the documented server path, which builds the GENERIC bulk store, so the
/// event store's own unit of work is what holds a batch together here. Each test uses fresh aggregate ids, so the
/// shared <c>__Events</c> table needs no cleanup. Skips without <c>BIRKO_*_HOST</c>; <c>BIRKO_REQUIRE_LIVE</c> makes
/// that a failure.
/// </summary>
public abstract class SqlEventStoreLiveTests<DB> where DB : AbstractConnector
{
    private readonly ITestOutputHelper _output;
    protected SqlEventStoreLiveTests(ITestOutputHelper output) => _output = output;

    protected abstract string Label { get; }
    protected abstract string HostVariable { get; }
    protected abstract SqlSettings Settings();

    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private bool RequireServer()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(HostVariable))) return true;
        var message = $"SKIPPED: no live {Label}. Set {HostVariable} to exercise this test; set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (RequireLive) throw new InvalidOperationException(message);
        return false;
    }

    private SqlEventStore<DB> NewStore() => new(Settings());

    private static DomainEvent Event(Guid aggregate, long version, string type = "Changed")
        => new(aggregate, version, type, $"{{\"v\":{version}}}") { Metadata = "{\"m\":1}", UserGuid = Guid.NewGuid() };

    [Fact]
    public async Task Round_trip_versions_and_slices()
    {
        if (!RequireServer()) return;
        var store = NewStore();
        var a = Guid.NewGuid();
        var events = Enumerable.Range(1, 4).Select(v => Event(a, v)).ToList();

        await store.AppendRangeAsync(events);

        var read = (await store.ReadAsync(a)).ToList();
        read.Select(e => e.Version).Should().Equal(1, 2, 3, 4);
        read[2].EventGuid.Should().Be(events[2].EventGuid);
        read[2].EventData.Should().Be("{\"v\":3}");
        read[2].Metadata.Should().Be("{\"m\":1}");
        read[2].UserGuid.Should().Be(events[2].UserGuid);
        read[2].OccurredAt.Should().BeCloseTo(events[2].OccurredAt, TimeSpan.FromSeconds(1));
        (await store.GetVersionAsync(a)).Should().Be(4);
        (await store.GetVersionAsync(Guid.NewGuid())).Should().Be(0);
        (await store.ReadUpToVersionAsync(a, 2)).Select(e => e.Version).Should().Equal(1, 2);
        (await store.ReadFromVersionAsync(a, 3)).Select(e => e.Version).Should().Equal(3, 4);
    }

    [Fact]
    public async Task A_taken_version_throws_the_typed_conflict_and_a_part_way_batch_leaves_nothing()
    {
        if (!RequireServer()) return;
        var a = Guid.NewGuid();
        await NewStore().AppendAsync(Event(a, 3, "Kept"));

        var single = async () => await NewStore().AppendAsync(Event(a, 3, "Second"));
        var thrown = (await single.Should().ThrowAsync<EventStoreConcurrencyException>()).Which;
        thrown.AggregateGuid.Should().Be(a);
        thrown.Version.Should().Be(3);

        var batch = async () => await NewStore().AppendRangeAsync(new[] { Event(a, 1), Event(a, 2), Event(a, 3), Event(a, 4) });
        (await batch.Should().ThrowAsync<EventStoreConcurrencyException>()).Which.Version.Should().Be(3);

        (await NewStore().ReadAsync(a)).Select(e => (e.Version, e.EventType)).Should().Equal((3L, "Kept"));
    }

    [Fact]
    public async Task Concurrent_writers_end_with_exactly_one_event_per_version()
    {
        if (!RequireServer()) return;
        var a = Guid.NewGuid();
        await NewStore().AppendAsync(Event(a, 1));
        var conflicts = 0;

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var store = NewStore();
                var next = await store.GetVersionAsync(a) + 1;
                try { await store.AppendAsync(Event(a, next)); }
                catch (EventStoreConcurrencyException) { Interlocked.Increment(ref conflicts); }
            }
        })));

        var versions = (await NewStore().ReadAsync(a)).Select(e => e.Version).ToList();
        _output.WriteLine($"{Label}: {versions.Count} events, {conflicts} conflicts");
        versions.Should().OnlyHaveUniqueItems();
        versions.Should().Equal(Enumerable.Range(1, versions.Count).Select(v => (long)v));
        conflicts.Should().BeGreaterThan(0, "the race must actually have been contested");
    }

    [Fact]
    public async Task An_append_inside_a_callers_unit_of_work_rolls_back_with_it()
    {
        if (!RequireServer()) return;
        var store = NewStore();
        var a = Guid.NewGuid();
        await store.AppendAsync(Event(a, 1));

        await using (var uow = SqlUnitOfWork.FromStore(store.Store))
        {
            await uow.BeginAsync();
            await store.AppendAsync(Event(a, 2));
            await uow.RollbackAsync();
        }
        (await store.GetVersionAsync(a)).Should().Be(1);

        await using (var uow = SqlUnitOfWork.FromStore(store.Store))
        {
            await uow.BeginAsync();
            await store.AppendAsync(Event(a, 2));
            await uow.CommitAsync();
        }
        (await store.GetVersionAsync(a)).Should().Be(2);
    }

    [Fact]
    public async Task A_conflict_inside_a_callers_unit_of_work_is_typed_and_poisons_the_boundary()
    {
        if (!RequireServer()) return;
        var store = NewStore();
        var a = Guid.NewGuid();
        await store.AppendAsync(Event(a, 1));

        await using var uow = SqlUnitOfWork.FromStore(store.Store);
        await uow.BeginAsync();
        var act = async () => await store.AppendAsync(Event(a, 1));
        (await act.Should().ThrowAsync<EventStoreConcurrencyException>()).Which.Version.Should().Be(1);

        var commit = async () => await uow.CommitAsync();
        await commit.Should().ThrowAsync<Exception>("the events were part of the caller's boundary, so it must not commit without them");
    }
}

public sealed class PostgreSqlEventStoreLiveTests : SqlEventStoreLiveTests<PostgreSQLConnector>
{
    public PostgreSqlEventStoreLiveTests(ITestOutputHelper output) : base(output) { }
    protected override string Label => "PostgreSQL";
    protected override string HostVariable => "BIRKO_PG_HOST";
    protected override SqlSettings Settings() => new PostgreSqlSettings(
        Environment.GetEnvironmentVariable("BIRKO_PG_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_PG_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_PG_USER") ?? "postgres",
        Environment.GetEnvironmentVariable("BIRKO_PG_PASSWORD") ?? "postgres")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_PG_PORT"), out var p) ? p : 5432 };
}

public sealed class MSSqlEventStoreLiveTests : SqlEventStoreLiveTests<MSSqlConnector>
{
    public MSSqlEventStoreLiveTests(ITestOutputHelper output) : base(output) { }
    protected override string Label => "SQL Server";
    protected override string HostVariable => "BIRKO_MSSQL_HOST";
    protected override SqlSettings Settings() => new MSSqlSettings(
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_USER") ?? "sa",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_PASSWORD") ?? "Birko!Passw0rd",
        int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MSSQL_PORT"), out var p) ? p : 1433)
    { TrustServerCertificate = true };
}

public sealed class MySqlEventStoreLiveTests : SqlEventStoreLiveTests<MySQLConnector>
{
    public MySqlEventStoreLiveTests(ITestOutputHelper output) : base(output) { }
    protected override string Label => "MySQL";
    protected override string HostVariable => "BIRKO_MYSQL_HOST";
    protected override SqlSettings Settings() => new MySqlSettings(
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_USER") ?? "root",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_PASSWORD") ?? "root",
        int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MYSQL_PORT"), out var p) ? p : 3306);
}
