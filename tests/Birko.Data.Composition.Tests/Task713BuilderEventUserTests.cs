using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Composition;
using Birko.Data.EventSourcing.Events;
using Birko.Data.EventSourcing.Models;
using Birko.Data.InMemory.Stores;
using Birko.Data.Models;
using Birko.Data.Patterns.Models;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Composition.Tests;

/// <summary>
/// TASK-713: the builder wrapped event sourcing without a user, and the only way to give it one was a settable
/// <c>CurrentUserGuid</c> on the shared wrapper, so every event built through it had <c>UserGuid == null</c>. It now
/// passes the audit context's user, read at each write.
/// </summary>
public class Task713BuilderEventUserTests
{
    public class Tracked : AbstractModel, IEventSourced
    {
        public long Version { get; set; }
        public void ApplyEvent(IEvent @event) { }
        public IEvent[] GetUncommittedEvents() => Array.Empty<IEvent>();
        public void MarkEventsAsCommitted() { }
        public void LoadFromEvents(IEnumerable<IEvent> events) { }
    }

    private sealed class Context : IAuditContext
    {
        public Guid? CurrentUserGuid { get; set; }
    }

    private sealed class Recording : IAsyncEventStore
    {
        public readonly List<IEvent> All = new();
        public Task AppendAsync(IEvent @event, CancellationToken ct = default) { All.Add(@event); return Task.CompletedTask; }
        public Task AppendRangeAsync(IEnumerable<IEvent> events, CancellationToken ct = default) { All.AddRange(events); return Task.CompletedTask; }
        public Task<IEnumerable<IEvent>> ReadAsync(Guid id, CancellationToken ct = default) => Task.FromResult<IEnumerable<IEvent>>(All.Where(e => e.AggregateGuid == id).ToList());
        public Task<IEnumerable<IEvent>> ReadUpToVersionAsync(Guid id, long max, CancellationToken ct = default) => ReadAsync(id, ct);
        public Task<IEnumerable<IEvent>> ReadFromVersionAsync(Guid id, long from, CancellationToken ct = default) => ReadAsync(id, ct);
        public Task<long> GetVersionAsync(Guid id, CancellationToken ct = default) => Task.FromResult(All.Where(e => e.AggregateGuid == id).Select(e => e.Version).DefaultIfEmpty(0).Max());
        public Task<IEnumerable<IEvent>> ReadAllFromAsync(DateTime from, CancellationToken ct = default) => Task.FromResult<IEnumerable<IEvent>>(All.ToList());
    }

    [Fact]
    public async Task Events_built_through_the_builder_carry_the_audit_context_s_user_at_each_write()
    {
        var context = new Context { CurrentUserGuid = Guid.NewGuid() };
        var events = new Recording();
        var store = StoreWrapperBuilder.Build<Tracked>(new AsyncInMemoryStore<Tracked>(), auditContext: context, eventStore: events);
        var first = context.CurrentUserGuid;

        var item = new Tracked();
        await store.CreateAsync(item);
        context.CurrentUserGuid = Guid.NewGuid();
        await store.UpdateAsync(item);

        events.All.Select(e => e.UserGuid).Should().Equal(first, context.CurrentUserGuid);
    }

    [Fact]
    public async Task Without_an_audit_context_the_events_carry_no_user()
    {
        var events = new Recording();
        var store = StoreWrapperBuilder.Build<Tracked>(new AsyncInMemoryStore<Tracked>(), eventStore: events);

        await store.CreateAsync(new Tracked());

        events.All.Single().UserGuid.Should().BeNull();
    }
}
