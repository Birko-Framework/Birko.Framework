using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.EventSourcing.Events;
using FluentAssertions;
using Xunit;

namespace Birko.Tests.Shared.EventSourcing;

/// <summary>
/// TASK-1000 — a batch cut short by a crash, on the backends whose event store appends a batch one event at a time (the
/// default <c>AsyncStoreEventStore</c>: InMemory, Elasticsearch, a standalone MongoDB). Shared source, linked into each
/// test project, which subclasses it.
/// </summary>
/// <remarks>
/// <see cref="NewCrashingWriterAsync"/> answers a writer over the same data whose store "dies" after a number of creates:
/// that create and every later call throw, so the writer can neither finish the batch nor remove what it wrote, as a
/// process killed mid-append. <see cref="RestartAsync"/> then brings the backend back for the other writers.
/// </remarks>
public abstract class CrashedBatchContract
{
    /// <summary>False when the backend is not available (a live server not configured); the test then does nothing.</summary>
    protected virtual bool Ready() => true;

    /// <summary>A new writer over the same stored events.</summary>
    protected abstract Task<IAsyncEventStore> NewStoreAsync();

    /// <summary>A writer over the same events whose store dies on create number <paramref name="createsBeforeCrash"/> + 1.</summary>
    protected abstract Task<IAsyncEventStore> NewCrashingWriterAsync(int createsBeforeCrash);

    /// <summary>Called after the crash, before anything else reads or writes; a backend shared in memory revives here.</summary>
    protected virtual Task RestartAsync() => Task.CompletedTask;

    /// <summary>Sets how long an incomplete batch must sit before an append may clear it.</summary>
    protected abstract void SetLease(IAsyncEventStore store, TimeSpan lease);

    private static DomainEvent Event(Guid aggregate, long version, string type = "Changed")
        => new(aggregate, version, type, $"{{\"v\":{version}}}");

    private static IEvent[] Range(Guid aggregate, long from, long to, string type = "Changed")
        => Enumerable.Range((int)from, (int)(to - from + 1)).Select(v => (IEvent)Event(aggregate, v, type)).ToArray();

    // v1 alone, then a batch v2..v4 whose writer dies after storing v2 and v3.
    private async Task<Guid> CrashMidBatchAsync()
    {
        var a = Guid.NewGuid();
        await (await NewStoreAsync()).AppendAsync(Event(a, 1, "Before"));
        var dying = await NewCrashingWriterAsync(2);
        await dying.Invoking(w => w.AppendRangeAsync(Range(a, 2, 4, "Cut"))).Should().ThrowAsync<Exception>();
        await RestartAsync();
        return a;
    }

    [Fact]
    public async Task A_batch_cut_short_by_a_crash_is_not_visible()
    {
        if (!Ready()) return;
        var a = await CrashMidBatchAsync();

        var store = await NewStoreAsync();
        (await store.ReadAsync(a)).Select(e => e.Version).Should().Equal(new long[] { 1 }, "the batch never completed");
        (await store.GetVersionAsync(a)).Should().Be(1);
        (await store.ReadUpToVersionAsync(a, 3)).Select(e => e.Version).Should().Equal(1);
        (await store.ReadFromVersionAsync(a, 2)).Should().BeEmpty();
        (await store.ReadAllFromAsync(DateTime.UtcNow.AddMinutes(-1))).Where(e => e.AggregateGuid == a)
            .Select(e => e.EventType).Should().Equal("Before");
    }

    [Fact]
    public async Task Within_the_lease_the_next_append_is_refused_and_nothing_changes()
    {
        if (!Ready()) return;
        var a = await CrashMidBatchAsync();

        var next = await NewStoreAsync();
        SetLease(next, TimeSpan.FromMinutes(5));
        var act = async () => await next.AppendAsync(Event(a, 2, "After"));
        (await act.Should().ThrowAsync<EventStoreConcurrencyException>()).Which.Version.Should().Be(2,
            "the incomplete batch may belong to a writer that is still running");
        (await (await NewStoreAsync()).ReadAsync(a)).Select(e => e.Version).Should().Equal(1);
    }

    [Fact]
    public async Task After_the_lease_the_next_append_clears_the_batch_and_lands()
    {
        if (!Ready()) return;
        var a = await CrashMidBatchAsync();

        var next = await NewStoreAsync();
        SetLease(next, TimeSpan.Zero);
        await next.AppendAsync(Event(a, 2, "After"));
        await next.AppendRangeAsync(Range(a, 3, 5, "Batch"));

        var history = (await (await NewStoreAsync()).ReadAsync(a)).ToList();
        history.Select(e => e.Version).Should().Equal(1, 2, 3, 4, 5);
        history.Select(e => e.EventType).Should().Equal("Before", "After", "Batch", "Batch", "Batch");
        (await (await NewStoreAsync()).GetVersionAsync(a)).Should().Be(5);
    }

    [Fact]
    public async Task A_batch_over_the_cut_versions_clears_them_after_the_lease()
    {
        if (!Ready()) return;
        var a = await CrashMidBatchAsync();

        var next = await NewStoreAsync();
        SetLease(next, TimeSpan.Zero);
        await next.AppendRangeAsync(Range(a, 2, 3, "Retry"));

        (await (await NewStoreAsync()).ReadAsync(a)).Select(e => e.EventType).Should().Equal("Before", "Retry", "Retry");
    }

    [Fact]
    public async Task A_completed_batch_is_never_cleared()
    {
        if (!Ready()) return;
        var a = Guid.NewGuid();
        await (await NewStoreAsync()).AppendRangeAsync(Range(a, 1, 3, "Whole"));

        var next = await NewStoreAsync();
        SetLease(next, TimeSpan.Zero);
        var act = async () => await next.AppendAsync(Event(a, 2, "Late"));
        (await act.Should().ThrowAsync<EventStoreConcurrencyException>()).Which.Version.Should().Be(2);
        (await (await NewStoreAsync()).ReadAsync(a)).Select(e => e.EventType).Should().Equal("Whole", "Whole", "Whole");
    }

    [Fact]
    public async Task A_batch_over_two_aggregates_shows_both_or_neither()
    {
        if (!Ready()) return;
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var dying = await NewCrashingWriterAsync(2);
        await dying.Invoking(w => w.AppendRangeAsync(new IEvent[] { Event(a, 1), Event(b, 1), Event(a, 2) }))
            .Should().ThrowAsync<Exception>();
        await RestartAsync();

        var store = await NewStoreAsync();
        (await store.ReadAsync(a)).Should().BeEmpty();
        (await store.ReadAsync(b)).Should().BeEmpty();

        var whole = await NewStoreAsync();
        SetLease(whole, TimeSpan.Zero);
        await whole.AppendRangeAsync(new IEvent[] { Event(a, 1), Event(b, 1), Event(a, 2) });
        (await (await NewStoreAsync()).ReadAsync(a)).Select(e => e.Version).Should().Equal(1, 2);
        (await (await NewStoreAsync()).ReadAsync(b)).Select(e => e.Version).Should().Equal(1);
    }
}
