using Birko.Data.EventSourcing.Events;
using Birko.Data.EventSourcing.Models;
using Birko.Data.EventSourcing.Stores;
using Birko.Data.InMemory.Stores;
using Birko.Data.Models;
using Birko.Data.Stores;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Birko.Data.EventSourcing.Tests;

/// <summary>
/// TASK-498 — the event-sourcing bulk wrappers route <c>Update(filter, PropertyUpdate)</c> through read-modify-save
/// (<c>PropertyUpdate.ApplyTo</c>) so that every change is recorded. An increment must therefore ADD to the stored
/// value (not assign the delta), only matching rows change, and each changed row gets an <c>Updated</c> event whose
/// payload carries the incremented value.
/// </summary>
public class PropertyUpdateIncrementEventSourcingTests
{
    public class Counter : AbstractModel, IEventSourced
    {
        public string Name { get; set; } = string.Empty;
        public int Hits { get; set; }
        public long Version { get; set; }

        private readonly List<IEvent> _uncommitted = new();
        public void ApplyEvent(IEvent @event) { Version = @event.Version; _uncommitted.Add(@event); }
        public IEvent[] GetUncommittedEvents() => _uncommitted.ToArray();
        public void MarkEventsAsCommitted() => _uncommitted.Clear();
        public void LoadFromEvents(IEnumerable<IEvent> events)
        {
            foreach (var e in events) ApplyEvent(e);
            MarkEventsAsCommitted();
        }
    }

    /// <summary>Minimal in-memory event store (sync + async) — the same double as <c>EventSourcingStoreWrapperTests</c>.</summary>
    private sealed class InMemoryEventStore : IEventStore, IAsyncEventStore
    {
        private readonly List<IEvent> _events = new();

        public void Append(IEvent @event) => _events.Add(@event);
        public void AppendRange(IEnumerable<IEvent> events) => _events.AddRange(events);
        public IEnumerable<IEvent> Read(Guid aggregateId) => _events.Where(e => e.AggregateId == aggregateId).OrderBy(e => e.Version).ToList();
        public IEnumerable<IEvent> ReadUpToVersion(Guid aggregateId, long maxVersion) => Read(aggregateId).Where(e => e.Version <= maxVersion);
        public IEnumerable<IEvent> ReadFromVersion(Guid aggregateId, long fromVersion) => Read(aggregateId).Where(e => e.Version >= fromVersion);
        public long GetVersion(Guid aggregateId) => _events.Where(e => e.AggregateId == aggregateId).Select(e => e.Version).DefaultIfEmpty(0).Max();
        public IEnumerable<IEvent> ReadAllFrom(DateTime from) => _events.Where(e => e.OccurredAt >= from);

        public Task AppendAsync(IEvent @event, CancellationToken ct = default) { Append(@event); return Task.CompletedTask; }
        public Task AppendRangeAsync(IEnumerable<IEvent> events, CancellationToken ct = default) { AppendRange(events); return Task.CompletedTask; }
        public Task<IEnumerable<IEvent>> ReadAsync(Guid aggregateId, CancellationToken ct = default) => Task.FromResult(Read(aggregateId));
        public Task<IEnumerable<IEvent>> ReadUpToVersionAsync(Guid aggregateId, long maxVersion, CancellationToken ct = default) => Task.FromResult(ReadUpToVersion(aggregateId, maxVersion));
        public Task<IEnumerable<IEvent>> ReadFromVersionAsync(Guid aggregateId, long fromVersion, CancellationToken ct = default) => Task.FromResult(ReadFromVersion(aggregateId, fromVersion));
        public Task<long> GetVersionAsync(Guid aggregateId, CancellationToken ct = default) => Task.FromResult(GetVersion(aggregateId));
        public Task<IEnumerable<IEvent>> ReadAllFromAsync(DateTime from, CancellationToken ct = default) => Task.FromResult(ReadAllFrom(from));
    }

    private static int HitsIn(IEvent @event)
    {
        using var doc = JsonDocument.Parse(@event.EventData);
        return doc.RootElement.EnumerateObject()
            .Single(p => string.Equals(p.Name, nameof(Counter.Hits), StringComparison.OrdinalIgnoreCase))
            .Value.GetInt32();
    }

    [Fact]
    public async Task Async_Increment_Adds_And_Records_An_Updated_Event()
    {
        var eventStore = new InMemoryEventStore();
        var inner = new AsyncInMemoryStore<Counter>();
        var wrapper = new AsyncEventSourcingBulkStoreWrapper<AsyncInMemoryStore<Counter>, Counter>(inner, eventStore);

        var target = new Counter { Name = "target", Hits = 10 };
        var bystander = new Counter { Name = "bystander", Hits = 10 };
        await wrapper.CreateAsync(new[] { target, bystander });

        await wrapper.UpdateAsync(x => x.Name == "target", new PropertyUpdate<Counter>().Increment(x => x.Hits, 5));

        (await inner.ReadAsync(target.Guid!.Value))!.Hits.Should().Be(15, "+5 on 10 — an assigned delta would read 5");
        (await inner.ReadAsync(bystander.Guid!.Value))!.Hits.Should().Be(10);

        var history = (await wrapper.GetHistoryAsync(target.Guid!.Value)).ToList();
        history.Select(e => e.EventType).Should().Equal("Created", "Updated");
        history[1].Version.Should().Be(2);
        HitsIn(history[1]).Should().Be(15, "the event records the value after the increment");

        (await wrapper.GetHistoryAsync(bystander.Guid!.Value)).Should().ContainSingle("a non-matching row gets no Updated event");
    }

    [Fact]
    public void Sync_Decrement_Subtracts_And_Records_An_Updated_Event()
    {
        var eventStore = new InMemoryEventStore();
        var inner = new InMemoryStore<Counter>();
        var wrapper = new EventSourcingBulkStoreWrapper<InMemoryStore<Counter>, Counter>(inner, eventStore);

        var target = new Counter { Name = "target", Hits = 10 };
        wrapper.Create(new[] { target });

        wrapper.Update(x => x.Name == "target", new PropertyUpdate<Counter>().Decrement(x => x.Hits, 3));

        inner.Read(target.Guid!.Value)!.Hits.Should().Be(7, "-3 on 10 — an assigned delta would read -3");

        var history = wrapper.GetHistory(target.Guid!.Value).ToList();
        history.Select(e => e.EventType).Should().Equal("Created", "Updated");
        HitsIn(history[1]).Should().Be(7);
    }
}
