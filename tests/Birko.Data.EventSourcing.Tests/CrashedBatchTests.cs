using Birko.Configuration;
using Birko.Data.EventSourcing.Events;
using Birko.Data.EventSourcing.Models;
using Birko.Data.EventSourcing.Stores;
using Birko.Data.InMemory.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.EventSourcing;
using FluentAssertions;
using Xunit;

namespace Birko.Data.EventSourcing.Tests;

/// <summary>An InMemory store that dies after a number of creates: that create and every later write throw until revived.</summary>
internal sealed class CrashingInMemoryStore : AsyncInMemoryStore<EventRecord>
{
    private int _createsLeft = int.MaxValue;
    private bool _dead;

    public CrashingInMemoryStore() => SetSettings(new Settings("t1000", "t1000"));

    public void CrashAfter(int creates) => _createsLeft = creates;

    public void Revive()
    {
        _dead = false;
        _createsLeft = int.MaxValue;
    }

    private void Gate(bool create)
    {
        if (_dead) throw new IOException("the process is dead");
        if (!create) return;
        if (_createsLeft-- > 0) return;
        _dead = true;
        throw new IOException("the process died");
    }

    protected override Task<Guid> CreateCoreAsync(EventRecord data, StoreDataDelegate<EventRecord>? processDelegate = null, CancellationToken ct = default)
    {
        Gate(true);
        return base.CreateCoreAsync(data, processDelegate, ct);
    }

    protected override Task DeleteCoreAsync(EventRecord data, CancellationToken ct = default)
    {
        Gate(false);
        return base.DeleteCoreAsync(data, ct);
    }

    protected override Task DeleteCoreAsync(IEnumerable<EventRecord> data, CancellationToken ct = default)
    {
        Gate(false);
        return base.DeleteCoreAsync(data, ct);
    }
}

/// <summary>TASK-1000 — a batch cut short by a crash, on the default store over InMemory.</summary>
public sealed class InMemoryCrashedBatchTests : CrashedBatchContract
{
    // One store shared by every writer, as InMemory has no second process; the crash is a state of it, revived after.
    private readonly CrashingInMemoryStore _store = new();

    protected override Task<IAsyncEventStore> NewStoreAsync() => Task.FromResult<IAsyncEventStore>(new AsyncStoreEventStore(_store));

    protected override Task<IAsyncEventStore> NewCrashingWriterAsync(int createsBeforeCrash)
    {
        _store.CrashAfter(createsBeforeCrash);
        return NewStoreAsync();
    }

    protected override Task RestartAsync()
    {
        _store.Revive();
        return Task.CompletedTask;
    }

    protected override void SetLease(IAsyncEventStore store, TimeSpan lease) => ((AsyncStoreEventStore)store).AbandonedBatchLease = lease;
}

/// <summary>An InMemory store whose create number <c>n</c> + 1 waits until released: a writer stalled mid-batch.</summary>
internal sealed class StallingInMemoryStore : AsyncInMemoryStore<EventRecord>
{
    private int _createsLeft = int.MaxValue;
    private TaskCompletionSource? _release;
    public readonly TaskCompletionSource Stalled = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public StallingInMemoryStore() => SetSettings(new Settings("t1000", "t1000"));

    public void StallAfter(int creates, TaskCompletionSource release)
    {
        _createsLeft = creates;
        _release = release;
    }

    protected override async Task<Guid> CreateCoreAsync(EventRecord data, StoreDataDelegate<EventRecord>? processDelegate = null, CancellationToken ct = default)
    {
        if (_release != null && _createsLeft-- == 0)
        {
            var release = _release;
            _release = null;
            Stalled.TrySetResult();
            await release.Task;
        }
        return await base.CreateCoreAsync(data, processDelegate, ct);
    }
}

/// <summary>TASK-1000 — a writer slower than the lease, whose batch another writer clears while it is still writing.</summary>
public sealed class AbandonedBatchRaceTests
{
    private static DomainEvent Event(Guid a, long v, string type) => new(a, v, type, "{}");

    [Fact]
    public async Task A_writer_whose_batch_was_cleared_while_it_stalled_fails_and_leaves_nothing()
    {
        var store = new StallingInMemoryStore();
        var a = Guid.NewGuid();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        store.StallAfter(1, release); // v1 stored, then stalls before v2

        var slow = new AsyncStoreEventStore(store).AppendRangeAsync(new IEvent[] { Event(a, 1, "Slow"), Event(a, 2, "Slow"), Event(a, 3, "Slow") });
        await store.Stalled.Task;

        var fast = new AsyncStoreEventStore(store) { AbandonedBatchLease = TimeSpan.Zero };
        await fast.AppendAsync(Event(a, 1, "Fast"));

        release.SetResult();
        var act = async () => await slow;
        (await act.Should().ThrowAsync<EventStoreConcurrencyException>()).Which.Version.Should().Be(3);

        var history = (await new AsyncStoreEventStore(store).ReadAsync(a)).ToList();
        history.Select(e => e.EventType).Should().Equal("Fast");
        (await store.ReadAsync(r => r.AggregateGuid == a, null, null, null)).Should().ContainSingle("the slow writer removed its own records");
    }

    [Fact]
    public async Task A_clearer_that_died_after_its_abort_marker_still_wins_and_the_next_append_finishes_the_job()
    {
        var store = new StallingInMemoryStore();
        var a = Guid.NewGuid();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        store.StallAfter(2, release); // v1, v2 stored, then stalls before v3

        var slow = new AsyncStoreEventStore(store).AppendRangeAsync(new IEvent[] { Event(a, 1, "Slow"), Event(a, 2, "Slow"), Event(a, 3, "Slow") });
        await store.Stalled.Task;

        // A clearing writer stored its abort marker under v3's id, then died before removing the batch.
        var batch = (await store.ReadAsync(r => r.AggregateGuid == a, null, null, null)).First().BatchGuid!.Value;
        var end = EventRecord.IdFor(a, 3);
        await store.CreateAsync(new EventRecord { Guid = end, BatchGuid = batch, BatchSize = 3, BatchEnd = end, Aborted = true, WrittenAt = DateTime.UtcNow });

        release.SetResult();
        (await FluentActions.Awaiting(async () => await slow).Should().ThrowAsync<EventStoreConcurrencyException>())
            .Which.Version.Should().Be(3, "its last event collided with the marker");
        (await new AsyncStoreEventStore(store).ReadAsync(a)).Should().BeEmpty();

        // Its own rollback removed the batch, marker included, so the next append (default lease) lands at once.
        await new AsyncStoreEventStore(store).AppendAsync(Event(a, 1, "Next"));
        (await new AsyncStoreEventStore(store).ReadAsync(a)).Select(e => e.EventType).Should().Equal("Next");
        (await store.ReadAsync(r => r.AggregateGuid == a || r.Guid == end, null, null, null)).Should().ContainSingle();
    }

    [Fact]
    public async Task A_batch_left_with_an_abort_marker_is_cleared_at_once_whatever_the_lease()
    {
        // The writer and the clearer both died: the batch's first records and the marker remain.
        var store = new CrashingInMemoryStore();
        var a = Guid.NewGuid();
        store.CrashAfter(2);
        await FluentActions.Awaiting(() => new AsyncStoreEventStore(store).AppendRangeAsync(new IEvent[] { Event(a, 1, "Cut"), Event(a, 2, "Cut"), Event(a, 3, "Cut") }))
            .Should().ThrowAsync<InvalidOperationException>("its rollback died too: the error names what remains");
        store.Revive();
        var batch = (await store.ReadAsync(r => r.AggregateGuid == a, null, null, null)).First().BatchGuid!.Value;
        var end = EventRecord.IdFor(a, 3);
        await store.CreateAsync(new EventRecord { Guid = end, BatchGuid = batch, BatchSize = 3, BatchEnd = end, Aborted = true, WrittenAt = DateTime.UtcNow });

        var next = new AsyncStoreEventStore(store) { AbandonedBatchLease = TimeSpan.FromHours(1) };
        (await next.GetVersionAsync(a)).Should().Be(0);
        await next.AppendAsync(Event(a, 1, "Next"));
        (await next.ReadAsync(a)).Select(e => e.EventType).Should().Equal("Next");
        (await store.ReadAsync(r => r.BatchGuid == batch, null, null, null)).Should().BeEmpty("the marker went with its batch");
    }

    [Fact]
    public void A_negative_lease_is_refused()
    {
        var store = new StallingInMemoryStore();
        FluentActions.Invoking(() => new AsyncStoreEventStore(store) { AbandonedBatchLease = TimeSpan.FromSeconds(-1) })
            .Should().Throw<ArgumentOutOfRangeException>();
    }
}

/// <summary>An InMemory store that counts the calls the event store makes.</summary>
internal sealed class CountingInMemoryStore : AsyncInMemoryStore<EventRecord>
{
    public int Creates;
    public int Reads;

    public CountingInMemoryStore() => SetSettings(new Settings("t1000", "t1000"));

    protected override Task<Guid> CreateCoreAsync(EventRecord data, StoreDataDelegate<EventRecord>? processDelegate = null, CancellationToken ct = default)
    {
        Creates++;
        return base.CreateCoreAsync(data, processDelegate, ct);
    }

    public override Task<IEnumerable<EventRecord>> ReadAsync(System.Linq.Expressions.Expression<Func<EventRecord, bool>>? filter = null, OrderBy<EventRecord>? orderBy = null, int? limit = null, int? offset = null, CancellationToken ct = default)
    {
        Reads++;
        return base.ReadAsync(filter, orderBy, limit, offset, ct);
    }
}

/// <summary>TASK-1000 — what the marking costs: nothing for single events, nothing for reads of whole batches.</summary>
public sealed class BatchMarkingCostTests
{
    [Fact]
    public async Task A_single_event_is_one_write_and_carries_no_batch()
    {
        var store = new CountingInMemoryStore();
        var a = Guid.NewGuid();
        await new AsyncStoreEventStore(store).AppendAsync(new DomainEvent(a, 1, "One", "{}"));

        store.Creates.Should().Be(1);
        store.Reads.Should().Be(0);
        (await store.ReadAsync(r => r.AggregateGuid == a, null, null, null)).Single().BatchGuid.Should().BeNull();
    }

    [Fact]
    public async Task A_batch_is_its_events_plus_one_check()
    {
        var store = new CountingInMemoryStore();
        var a = Guid.NewGuid();
        await new AsyncStoreEventStore(store).AppendRangeAsync(Enumerable.Range(1, 4).Select(v => (IEvent)new DomainEvent(a, v, "T", "{}")));

        store.Creates.Should().Be(4);
        store.Reads.Should().Be(1, "after the last event, the writer checks its batch is whole");
    }

    [Fact]
    public async Task Reading_whole_batches_and_single_events_costs_one_query()
    {
        var store = new CountingInMemoryStore();
        var a = Guid.NewGuid();
        var events = new AsyncStoreEventStore(store);
        await events.AppendRangeAsync(Enumerable.Range(1, 3).Select(v => (IEvent)new DomainEvent(a, v, "T", "{}")));
        await events.AppendAsync(new DomainEvent(a, 4, "T", "{}"));
        await events.AppendRangeAsync(Enumerable.Range(5, 2).Select(v => (IEvent)new DomainEvent(a, v, "T", "{}")));

        store.Reads = 0;
        (await events.ReadAsync(a)).Should().HaveCount(6);
        (await events.GetVersionAsync(a)).Should().Be(6);
        store.Reads.Should().Be(2, "one query each; every batch is in hand");
    }
}
