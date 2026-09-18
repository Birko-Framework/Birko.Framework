using Birko.Data.EventSourcing.Events;
using Birko.Data.EventSourcing.Models;
using Birko.Data.EventSourcing.Stores;
using Birko.Data.InMemory.Stores;
using Birko.Data.Models;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Birko.Data.EventSourcing.Tests;

/// <summary>
/// Regression tests for CR-C05 / CR-C06: the Created-event AggregateId must match the Guid the
/// inner store actually persists the row under. Previously the wrapper built the event from a
/// throwaway <c>Guid.NewGuid()</c> that was never written back to the item, so the inner store
/// assigned a different Guid and Replay/GetHistory keyed by the persisted Guid found no event.
/// </summary>
public class EventSourcingStoreWrapperTests
{
    public class TestModel : AbstractModel, IEventSourced
    {
        public string Name { get; set; } = string.Empty;

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

    /// <summary>Minimal in-memory event store test double (sync + async).</summary>
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

    // CR-C05 — single Create
    [Fact]
    public void Create_RecordsEventUnderThePersistedGuid()
    {
        var eventStore = new InMemoryEventStore();
        var inner = new InMemoryStore<TestModel>();
        var wrapper = new EventSourcingStoreWrapper<InMemoryStore<TestModel>, TestModel>(inner, eventStore);

        var model = new TestModel { Name = "alpha" };
        var persistedGuid = wrapper.Create(model);

        // The item ends up persisted under the same Guid the wrapper used for the event.
        model.Guid.Should().Be(persistedGuid);

        // The Created event is retrievable by the persisted Guid (was orphaned before the fix).
        var history = wrapper.GetHistory(persistedGuid).ToList();
        history.Should().ContainSingle();
        history[0].EventType.Should().Be("Created");
        history[0].AggregateId.Should().Be(persistedGuid);

        // The row is actually in the inner store under that Guid.
        inner.Read(persistedGuid).Should().NotBeNull();
    }

    // CR-C06 — bulk Create (sync)
    [Fact]
    public void BulkCreate_RecordsEventsUnderEachPersistedGuid()
    {
        var eventStore = new InMemoryEventStore();
        var inner = new InMemoryStore<TestModel>();
        var wrapper = new EventSourcingBulkStoreWrapper<InMemoryStore<TestModel>, TestModel>(inner, eventStore);

        var models = new List<TestModel> { new() { Name = "a" }, new() { Name = "b" } };
        wrapper.Create(models);

        foreach (var m in models)
        {
            m.Guid.Should().NotBeNull();
            var history = wrapper.GetHistory(m.Guid!.Value).ToList();
            history.Should().ContainSingle("each created row must have its Created event under its own Guid");
            history[0].AggregateId.Should().Be(m.Guid.Value);
            inner.Read(m.Guid.Value).Should().NotBeNull();
        }
    }

    // CR-H048 — version sequencing across create/update/delete
    [Fact]
    public void CreateUpdateDelete_RecordsMonotonicVersionsAndEventTypes()
    {
        var eventStore = new InMemoryEventStore();
        var inner = new InMemoryStore<TestModel>();
        var wrapper = new EventSourcingStoreWrapper<InMemoryStore<TestModel>, TestModel>(inner, eventStore);

        var model = new TestModel { Name = "v1" };
        var guid = wrapper.Create(model);
        model.Name = "v2"; wrapper.Update(model);
        model.Name = "v3"; wrapper.Update(model);
        wrapper.Delete(model);

        var history = wrapper.GetHistory(guid).ToList();

        history.Select(e => e.EventType).Should().Equal("Created", "Updated", "Updated", "Deleted");
        history.Select(e => e.Version).Should().Equal(1, 2, 3, 4);
        history.Should().OnlyContain(e => e.AggregateId == guid);
    }

    // CR-H048 — Replay reconstructs the aggregate to its latest version
    [Fact]
    public void Replay_ReconstructsLatestVersion()
    {
        var eventStore = new InMemoryEventStore();
        var inner = new InMemoryStore<TestModel>();
        var wrapper = new EventSourcingStoreWrapper<InMemoryStore<TestModel>, TestModel>(inner, eventStore);

        var model = new TestModel { Name = "a" };
        var guid = wrapper.Create(model);
        model.Name = "b"; wrapper.Update(model);

        var replayed = wrapper.Replay(guid);

        replayed.Version.Should().Be(2, "Replay applies every event, ending at the latest version");
    }

    // CR-C06 — bulk Create (async)
    [Fact]
    public async Task BulkCreateAsync_RecordsEventsUnderEachPersistedGuid()
    {
        var eventStore = new InMemoryEventStore();
        var inner = new AsyncInMemoryStore<TestModel>();
        var wrapper = new AsyncEventSourcingBulkStoreWrapper<AsyncInMemoryStore<TestModel>, TestModel>(inner, eventStore);

        var models = new List<TestModel> { new() { Name = "a" }, new() { Name = "b" } };
        await wrapper.CreateAsync(models);

        foreach (var m in models)
        {
            m.Guid.Should().NotBeNull();
            var history = (await wrapper.GetHistoryAsync(m.Guid!.Value)).ToList();
            history.Should().ContainSingle();
            history[0].AggregateId.Should().Be(m.Guid.Value);
        }
    }
}
