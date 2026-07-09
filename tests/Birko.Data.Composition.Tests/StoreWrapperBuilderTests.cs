using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Composition;
using Birko.Data.EventSourcing.Events;
using Birko.Data.EventSourcing.Models;
using Birko.Data.InMemory.Stores;
using Birko.Data.Models;
using Birko.Data.Patterns.Models;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Composition.Tests;

/// <summary>
/// CR-M081: StoreWrapperBuilder.Build&lt;T&gt; is the project's entire public surface — 8 conditional
/// decorator branches, context-gating, a specific outermost→innermost order, and reflection-based
/// generic construction (which would only fail at runtime on a constructor-signature drift). These
/// tests exercise it over the InMemory raw store and pin every exercised wrapper's constructor arity.
/// </summary>
public class StoreWrapperBuilderTests
{
    #region Test models

    public class PlainModel : AbstractModel { }

    public class DefaultModel : AbstractModel, IDefault
    {
        public bool IsDefault { get; set; }
    }

    public class AuditableModel : AbstractModel, IAuditable
    {
        public Guid? CreatedBy { get; set; }
        public Guid? UpdatedBy { get; set; }
    }

    public class SoftDeleteModel : AbstractModel, ISoftDeletable
    {
        public DateTime? DeletedAt { get; set; }
    }

    // IDefault (outer) + ISoftDeletable (inner) — for chain-ordering.
    public class DefaultAndSoftDeleteModel : AbstractModel, IDefault, ISoftDeletable
    {
        public bool IsDefault { get; set; }
        public DateTime? DeletedAt { get; set; }
    }

    public class EventSourcedModel : AbstractModel, IEventSourced
    {
        private readonly List<IEvent> _uncommitted = new();
        public long Version { get; set; }
        public void ApplyEvent(IEvent @event) => _uncommitted.Add(@event);
        public IEvent[] GetUncommittedEvents() => _uncommitted.ToArray();
        public void MarkEventsAsCommitted() => _uncommitted.Clear();
        public void LoadFromEvents(IEnumerable<IEvent> events) { }
    }

    #endregion

    #region Test doubles

    private sealed class FakeAuditContext : IAuditContext
    {
        public Guid? CurrentUserId => Guid.NewGuid();
    }

    private sealed class FakeAsyncEventStore : IAsyncEventStore
    {
        public Task AppendAsync(IEvent @event, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AppendRangeAsync(IEnumerable<IEvent> events, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IEnumerable<IEvent>> ReadAsync(Guid aggregateId, CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<IEvent>>(Array.Empty<IEvent>());
        public Task<IEnumerable<IEvent>> ReadUpToVersionAsync(Guid aggregateId, long maxVersion, CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<IEvent>>(Array.Empty<IEvent>());
        public Task<IEnumerable<IEvent>> ReadFromVersionAsync(Guid aggregateId, long fromVersion, CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<IEvent>>(Array.Empty<IEvent>());
        public Task<long> GetVersionAsync(Guid aggregateId, CancellationToken cancellationToken = default) => Task.FromResult(0L);
        public Task<IEnumerable<IEvent>> ReadAllFromAsync(DateTime from, CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<IEvent>>(Array.Empty<IEvent>());
    }

    #endregion

    private static IAsyncBulkStore<T> Raw<T>() where T : AbstractModel, new() => new AsyncInMemoryStore<T>();

    private static object? Inner(object store) => ((IStoreWrapper)store).GetInnerStore();

    [Fact]
    public void Plain_model_gets_no_wrappers()
    {
        var raw = Raw<PlainModel>();

        var result = StoreWrapperBuilder.Build<PlainModel>(raw);

        result.Should().BeSameAs(raw, "a model implementing no marker interface must return the raw store");
    }

    [Fact]
    public void Default_marker_triggers_the_default_wrapper_over_the_raw_store()
    {
        var raw = Raw<DefaultModel>();

        var result = StoreWrapperBuilder.Build<DefaultModel>(raw);

        result.Should().NotBeSameAs(raw);
        result.Should().BeAssignableTo<IStoreWrapper>();
        result.GetType().Name.Should().StartWith("AsyncDefaultStoreWrapper");
        Inner(result).Should().BeSameAs(raw);
    }

    [Fact]
    public void SoftDelete_marker_triggers_its_wrapper_without_any_context()
    {
        var raw = Raw<SoftDeleteModel>();

        var result = StoreWrapperBuilder.Build<SoftDeleteModel>(raw);

        result.Should().NotBeSameAs(raw);
        result.GetType().Name.Should().Contain("SoftDelete");
        Inner(result).Should().BeSameAs(raw);
    }

    [Fact]
    public void Auditable_marker_is_skipped_when_no_audit_context_is_provided()
    {
        var raw = Raw<AuditableModel>();

        var result = StoreWrapperBuilder.Build<AuditableModel>(raw, auditContext: null);

        result.Should().BeSameAs(raw, "the audit wrapper is gated on a non-null IAuditContext");
    }

    [Fact]
    public void Auditable_marker_triggers_the_audit_wrapper_when_context_is_provided()
    {
        var raw = Raw<AuditableModel>();

        var result = StoreWrapperBuilder.Build<AuditableModel>(raw, auditContext: new FakeAuditContext());

        result.Should().NotBeSameAs(raw);
        result.GetType().Name.Should().Contain("Audit");
        Inner(result).Should().BeSameAs(raw);
    }

    [Fact]
    public void Chain_order_is_default_outermost_then_softdelete_then_raw()
    {
        var raw = Raw<DefaultAndSoftDeleteModel>();

        var result = StoreWrapperBuilder.Build<DefaultAndSoftDeleteModel>(raw);

        // Outermost: Default (applied last, no tenant/audit context here).
        result.GetType().Name.Should().StartWith("AsyncDefaultStoreWrapper");

        // Next inward: SoftDelete.
        var middle = Inner(result);
        middle.Should().NotBeNull();
        middle!.GetType().Name.Should().Contain("SoftDelete");

        // Innermost: the raw store.
        Inner(middle).Should().BeSameAs(raw);
    }

    [Fact]
    public void EventSourcing_branch_is_skipped_when_no_event_store_is_provided()
    {
        var raw = Raw<EventSourcedModel>();

        var result = StoreWrapperBuilder.Build<EventSourcedModel>(raw, eventStore: null);

        result.Should().BeSameAs(raw, "the event-sourcing wrapper is gated on a non-null IAsyncEventStore");
    }

    [Fact]
    public void EventSourcing_branch_constructs_when_an_event_store_is_provided()
    {
        // Guards the positional null serializer argument the builder passes (CR-M081): a ctor-arity
        // drift on AsyncEventSourcingBulkStoreWrapper would surface here as a MissingMethodException.
        var raw = Raw<EventSourcedModel>();

        var result = StoreWrapperBuilder.Build<EventSourcedModel>(raw, eventStore: new FakeAsyncEventStore());

        result.Should().NotBeSameAs(raw);
        result.GetType().Name.Should().Contain("EventSourcing");
        Inner(result).Should().BeSameAs(raw);
    }
}
