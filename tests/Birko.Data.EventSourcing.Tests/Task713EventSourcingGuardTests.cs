using Birko.Data.EventSourcing.Events;
using Birko.Data.EventSourcing.Models;
using Birko.Data.EventSourcing.Stores;
using Birko.Data.InMemory.Stores;
using Birko.Data.Models;
using Birko.Serialization.Json;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Birko.Data.EventSourcing.Tests;

/// <summary>
/// TASK-713. Measured before the fix: an event's <c>EventData</c> carried the entity's previous <c>Version</c>; the
/// filtered update and delete of the bulk wrappers took a null filter, or one that reduces to every row, and rewrote / deleted every row,
/// with an event each; an item with no Guid in a bulk update got no event while its siblings' events were appended
/// before the inner store refused the call (orphans); and the user stamped on events came only from a settable
/// <c>CurrentUserGuid</c> on the wrapper, per-caller state on an object the builder shares, which it never set.
/// </summary>
public class Task713EventSourcingGuardTests
{
    public class Item : AbstractModel, IEventSourced
    {
        public string Name { get; set; } = string.Empty;
        public long Version { get; set; }
        public void ApplyEvent(IEvent @event) => Version = @event.Version;
        public IEvent[] GetUncommittedEvents() => Array.Empty<IEvent>();
        public void MarkEventsAsCommitted() { }
        public void LoadFromEvents(IEnumerable<IEvent> events) { foreach (var e in events) ApplyEvent(e); }
    }

    private sealed class Events : IEventStore, IAsyncEventStore
    {
        public readonly List<IEvent> All = new();
        public void Append(IEvent @event) => All.Add(@event);
        public void AppendRange(IEnumerable<IEvent> events) => All.AddRange(events);
        public IEnumerable<IEvent> Read(Guid id) => All.Where(e => e.AggregateGuid == id).OrderBy(e => e.Version).ToList();
        public IEnumerable<IEvent> ReadUpToVersion(Guid id, long max) => Read(id).Where(e => e.Version <= max);
        public IEnumerable<IEvent> ReadFromVersion(Guid id, long from) => Read(id).Where(e => e.Version >= from);
        public long GetVersion(Guid id) => All.Where(e => e.AggregateGuid == id).Select(e => e.Version).DefaultIfEmpty(0).Max();
        public IEnumerable<IEvent> ReadAllFrom(DateTime from) => All.Where(e => e.OccurredAt >= from);
        public Task AppendAsync(IEvent @event, CancellationToken ct = default) { Append(@event); return Task.CompletedTask; }
        public Task AppendRangeAsync(IEnumerable<IEvent> events, CancellationToken ct = default) { AppendRange(events); return Task.CompletedTask; }
        public Task<IEnumerable<IEvent>> ReadAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Read(id));
        public Task<IEnumerable<IEvent>> ReadUpToVersionAsync(Guid id, long max, CancellationToken ct = default) => Task.FromResult(ReadUpToVersion(id, max));
        public Task<IEnumerable<IEvent>> ReadFromVersionAsync(Guid id, long from, CancellationToken ct = default) => Task.FromResult(ReadFromVersion(id, from));
        public Task<long> GetVersionAsync(Guid id, CancellationToken ct = default) => Task.FromResult(GetVersion(id));
        public Task<IEnumerable<IEvent>> ReadAllFromAsync(DateTime from, CancellationToken ct = default) => Task.FromResult(ReadAllFrom(from));
    }

    private static long PayloadVersion(IEvent e) => new SystemJsonSerializer().Deserialize<Item>(e.EventData)!.Version;

    private static (AsyncEventSourcingBulkStoreWrapper<AsyncInMemoryStore<Item>, Item> wrapper, Events events, AsyncInMemoryStore<Item> inner) Async(Func<Guid?>? user = null)
    {
        var events = new Events();
        var inner = new AsyncInMemoryStore<Item>();
        return (new AsyncEventSourcingBulkStoreWrapper<AsyncInMemoryStore<Item>, Item>(inner, events, currentUser: user), events, inner);
    }

    private static (EventSourcingBulkStoreWrapper<InMemoryStore<Item>, Item> wrapper, Events events, InMemoryStore<Item> inner) Sync(Func<Guid?>? user = null)
    {
        var events = new Events();
        var inner = new InMemoryStore<Item>();
        return (new EventSourcingBulkStoreWrapper<InMemoryStore<Item>, Item>(inner, events, currentUser: user), events, inner);
    }

    // ---- the payload carries the version it is the event of ----

    [Fact]
    public async Task Each_event_s_payload_carries_its_own_version()
    {
        var (wrapper, events, _) = Async();
        var item = new Item { Name = "a" };
        await wrapper.CreateAsync(item);
        item.Name = "b";
        await wrapper.UpdateAsync(item);
        await wrapper.UpdateAsync(x => x.Name == "b", x => x.Name = "c");
        await wrapper.DeleteAsync(new[] { item });

        events.All.Select(e => (e.Version, PayloadVersion(e))).Should().Equal((1L, 1L), (2L, 2L), (3L, 3L), (4L, 4L));
    }

    [Fact]
    public void Each_sync_event_s_payload_carries_its_own_version()
    {
        var (wrapper, events, _) = Sync();
        var item = new Item { Name = "a" };
        wrapper.Create(item);
        item.Name = "b";
        wrapper.Update(item);

        events.All.Select(e => (e.Version, PayloadVersion(e))).Should().Equal((1L, 1L), (2L, 2L));
    }

    // ---- the filtered writes refuse a filter that covers every row ----

    // x => true is the framework's explicit all-rows filter and is let through (PredicateScope); a filter that only REDUCES
    // to every row is the mistake the guard catches.
    private static readonly List<string> Nobody = new();

    public static TheoryData<string> WholeTable => new() { "update-null", "update-reduces", "patch-reduces", "delete-null", "delete-reduces" };

    [Theory]
    [MemberData(nameof(WholeTable))]
    public async Task A_filtered_write_over_every_row_is_refused_and_records_nothing(string kind)
    {
        var (wrapper, events, inner) = Async();
        await wrapper.CreateAsync(new[] { new Item { Name = "a" }, new Item { Name = "b" } });
        var before = events.All.Count;

        Func<Task> act = kind switch
        {
            "update-null" => () => wrapper.UpdateAsync(null!, (Action<Item>)(x => x.Name = "z")),
            "update-reduces" => () => wrapper.UpdateAsync(x => !Nobody.Contains(x.Name), (Action<Item>)(x => x.Name = "z")),
            "patch-reduces" => () => wrapper.UpdateAsync(x => !Nobody.Contains(x.Name), new Data.Stores.PropertyUpdate<Item>().Set(x => x.Name, "z")),
            "delete-null" => () => wrapper.DeleteAsync((System.Linq.Expressions.Expression<Func<Item, bool>>)null!),
            _ => () => wrapper.DeleteAsync(x => !Nobody.Contains(x.Name)),
        };

        await act.Should().ThrowAsync<Exception>();
        events.All.Should().HaveCount(before);
        (await inner.ReadAsync(x => x.Name != "z", null, null, null)).Should().HaveCount(2);
    }

    [Theory]
    [MemberData(nameof(WholeTable))]
    public void A_sync_filtered_write_over_every_row_is_refused_and_records_nothing(string kind)
    {
        var (wrapper, events, inner) = Sync();
        wrapper.Create(new[] { new Item { Name = "a" }, new Item { Name = "b" } });
        var before = events.All.Count;

        Action act = kind switch
        {
            "update-null" => () => wrapper.Update(null!, (Action<Item>)(x => x.Name = "z")),
            "update-reduces" => () => wrapper.Update(x => !Nobody.Contains(x.Name), (Action<Item>)(x => x.Name = "z")),
            "patch-reduces" => () => wrapper.Update(x => !Nobody.Contains(x.Name), new Data.Stores.PropertyUpdate<Item>().Set(x => x.Name, "z")),
            "delete-null" => () => wrapper.Delete((System.Linq.Expressions.Expression<Func<Item, bool>>)null!),
            _ => () => wrapper.Delete(x => !Nobody.Contains(x.Name)),
        };

        act.Should().Throw<Exception>();
        events.All.Should().HaveCount(before);
        inner.Read(x => x.Name != "z", null, null, null).Should().HaveCount(2);
    }

    // ---- an item without a Guid refuses the whole call before any event ----

    [Fact]
    public async Task A_bulk_update_with_an_item_without_a_guid_records_no_event()
    {
        var (wrapper, events, _) = Async();
        var stored = new Item { Name = "a" };
        await wrapper.CreateAsync(stored);
        var before = events.All.Count;

        var act = () => wrapper.UpdateAsync(new[] { stored, new Item { Name = "nobody" } });

        await act.Should().ThrowAsync<Exception>();
        events.All.Should().HaveCount(before, "no event for a call the store refuses");
    }

    [Fact]
    public void A_sync_bulk_update_with_an_item_without_a_guid_records_no_event()
    {
        var (wrapper, events, _) = Sync();
        var stored = new Item { Name = "a" };
        wrapper.Create(stored);
        var before = events.All.Count;

        var act = () => wrapper.Update(new[] { stored, new Item { Name = "nobody" } });

        act.Should().Throw<Exception>();
        events.All.Should().HaveCount(before);
    }

    // ---- the user is read per write, from what the caller supplies ----

    [Fact]
    public async Task The_user_is_read_at_each_write()
    {
        Guid? current = Guid.NewGuid();
        var first = current;
        var (wrapper, events, _) = Async(() => current);
        var item = new Item { Name = "a" };
        await wrapper.CreateAsync(item);
        current = Guid.NewGuid();
        await wrapper.UpdateAsync(item);

        events.All.Select(e => e.UserGuid).Should().Equal(first, current);
    }
}
