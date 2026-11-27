using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.EventSourcing.Events;
using FluentAssertions;
using Xunit;

namespace Birko.Tests.Shared.EventSourcing;

/// <summary>
/// TASK-995 — the <see cref="IAsyncEventStore"/> contract, one suite for every implementation: <c>SqlEventStore</c>, the
/// default <c>AsyncStoreEventStore</c> on any backend, and each platform override. Shared source, linked into each test
/// project, which subclasses it.
/// </summary>
/// <remarks>
/// <see cref="NewStoreAsync"/> answers a NEW writer over the SAME data each call — a second client, as in a second
/// process — so the concurrency tests are not answered by one object's memory. Every test works on aggregates of its own,
/// so a store that keeps data between tests (a live server) is fine.
/// </remarks>
public abstract class EventStoreContract
{
    /// <summary>False when the backend is not available (a live server not configured); the test then does nothing.</summary>
    protected virtual bool Ready() => true;

    /// <summary>A new writer over the same stored events.</summary>
    protected abstract Task<IAsyncEventStore> NewStoreAsync();

    private static DomainEvent Event(Guid aggregate, long version, string type = "Changed")
        => new(aggregate, version, type, $"{{\"v\":{version}}}");

    private static async Task<List<long>> VersionsAsync(IAsyncEventStore store, Guid aggregate)
        => (await store.ReadAsync(aggregate)).Select(e => e.Version).ToList();

    [Fact]
    public async Task Appended_events_read_back_in_version_order_with_every_field()
    {
        if (!Ready()) return;
        var store = await NewStoreAsync();
        var a = Guid.NewGuid();
        var user = Guid.NewGuid();
        var second = new DomainEvent(a, 2, "Renamed", "{\"name\":\"x\"}", user) { Metadata = "{\"by\":\"test\"}" };
        await store.AppendRangeAsync(new IEvent[] { Event(a, 1, "Created"), second });
        await store.AppendAsync(Event(a, 3));

        var read = (await (await NewStoreAsync()).ReadAsync(a)).ToList();
        read.Select(e => e.Version).Should().Equal(1, 2, 3);
        read.Select(e => e.EventType).Should().Equal("Created", "Renamed", "Changed");
        var r = read[1];
        r.EventGuid.Should().Be(second.EventGuid);
        r.AggregateGuid.Should().Be(a);
        r.EventData.Should().Be("{\"name\":\"x\"}");
        r.Metadata.Should().Be("{\"by\":\"test\"}");
        r.UserGuid.Should().Be(user);
        r.OccurredAt.ToUniversalTime().Should().BeCloseTo(second.OccurredAt, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Version_ranges_the_current_version_and_reads_from_a_time()
    {
        if (!Ready()) return;
        var store = await NewStoreAsync();
        var a = Guid.NewGuid();
        await store.AppendRangeAsync(Enumerable.Range(1, 5).Select(v => (IEvent)Event(a, v)));

        (await store.ReadUpToVersionAsync(a, 3)).Select(e => e.Version).Should().Equal(1, 2, 3);
        (await store.ReadFromVersionAsync(a, 4)).Select(e => e.Version).Should().Equal(4, 5);
        (await store.GetVersionAsync(a)).Should().Be(5);

        var mine = (await store.ReadAllFromAsync(DateTime.UtcNow.AddMinutes(-1))).Where(e => e.AggregateGuid == a).ToList();
        mine.Select(e => e.Version).Should().Equal(1, 2, 3, 4, 5);
        (await store.ReadAllFromAsync(DateTime.UtcNow.AddMinutes(1))).Should().NotContain(e => e.AggregateGuid == a);
    }

    [Fact]
    public async Task An_aggregate_with_no_events_reads_empty_at_version_0()
    {
        if (!Ready()) return;
        var store = await NewStoreAsync();
        var none = Guid.NewGuid();
        (await store.ReadAsync(none)).Should().BeEmpty();
        (await store.GetVersionAsync(none)).Should().Be(0);
    }

    [Fact]
    public async Task The_current_version_is_visible_right_after_an_append()
    {
        if (!Ready()) return;
        var a = Guid.NewGuid();
        await (await NewStoreAsync()).AppendAsync(Event(a, 1));
        (await (await NewStoreAsync()).GetVersionAsync(a)).Should().Be(1, "a writer that reloads must see the version it just wrote");
    }

    [Fact]
    public async Task A_taken_version_is_refused_and_the_first_event_stays()
    {
        if (!Ready()) return;
        var a = Guid.NewGuid();
        await (await NewStoreAsync()).AppendAsync(Event(a, 1, "First"));

        var again = async () => await (await NewStoreAsync()).AppendAsync(Event(a, 1, "Second"));
        var thrown = (await again.Should().ThrowAsync<EventStoreConcurrencyException>()).Which;
        thrown.AggregateGuid.Should().Be(a);
        thrown.Version.Should().Be(1);
        (await (await NewStoreAsync()).ReadAsync(a)).Single().EventType.Should().Be("First");
    }

    [Fact]
    public async Task A_batch_that_hits_a_taken_version_keeps_nothing()
    {
        if (!Ready()) return;
        var a = Guid.NewGuid();
        await (await NewStoreAsync()).AppendAsync(Event(a, 3, "Taken"));

        var batch = async () => await (await NewStoreAsync()).AppendRangeAsync(new IEvent[] { Event(a, 1), Event(a, 2), Event(a, 3), Event(a, 4) });
        (await batch.Should().ThrowAsync<EventStoreConcurrencyException>()).Which.Version.Should().Be(3);
        (await VersionsAsync(await NewStoreAsync(), a)).Should().Equal(3);
    }

    [Fact]
    public async Task A_batch_holding_one_version_twice_is_refused_and_keeps_nothing()
    {
        if (!Ready()) return;
        var a = Guid.NewGuid();
        var batch = async () => await (await NewStoreAsync()).AppendRangeAsync(new IEvent[] { Event(a, 1), Event(a, 2), Event(a, 2) });
        (await batch.Should().ThrowAsync<EventStoreConcurrencyException>()).Which.Version.Should().Be(2);
        (await VersionsAsync(await NewStoreAsync(), a)).Should().BeEmpty();
    }

    [Fact]
    public async Task Null_and_empty_input()
    {
        if (!Ready()) return;
        var store = await NewStoreAsync();
        await store.Invoking(s => s.AppendAsync(null!)).Should().ThrowAsync<ArgumentNullException>();
        await store.Invoking(s => s.AppendRangeAsync(null!)).Should().ThrowAsync<ArgumentNullException>();
        await store.Invoking(s => s.AppendRangeAsync(new IEvent[] { Event(Guid.NewGuid(), 1), null! })).Should().ThrowAsync<ArgumentException>();
        await store.AppendRangeAsync(Array.Empty<IEvent>());
    }

    /// <summary>
    /// DraCode's test: two writers change one aggregate at once, each reloading and retrying on a conflict. Both land, every
    /// version exactly once, and the history reads back in order.
    /// </summary>
    [Fact]
    public async Task Two_writers_changing_one_aggregate_at_once_land_on_distinct_versions()
    {
        if (!Ready()) return;
        var a = Guid.NewGuid();
        const int perWriter = 5;
        var conflicts = 0;
        var gate = new TaskCompletionSource();

        async Task Writer(string name)
        {
            var store = await NewStoreAsync();
            await gate.Task;
            for (var done = 0; done < perWriter;)
            {
                var next = await store.GetVersionAsync(a) + 1;
                try
                {
                    await store.AppendAsync(Event(a, next, name));
                    done++;
                }
                catch (EventStoreConcurrencyException)
                {
                    Interlocked.Increment(ref conflicts);
                }
            }
        }

        var writers = new[] { Task.Run(() => Writer("A")), Task.Run(() => Writer("B")) };
        gate.SetResult();
        await Task.WhenAll(writers).WaitAsync(TimeSpan.FromMinutes(2));

        var history = (await (await NewStoreAsync()).ReadAsync(a)).ToList();
        history.Select(e => e.Version).Should().Equal(Enumerable.Range(1, 2 * perWriter).Select(v => (long)v));
        history.Count(e => e.EventType == "A").Should().Be(perWriter);
        history.Count(e => e.EventType == "B").Should().Be(perWriter);
    }
}
