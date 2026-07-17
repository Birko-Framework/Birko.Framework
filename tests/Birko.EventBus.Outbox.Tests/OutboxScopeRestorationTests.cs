using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.EventBus;
using Birko.EventBus.Outbox;
using Birko.EventBus.Outbox.Publishing;
using Birko.EventBus.Outbox.Stores;
using Birko.MessageQueue.Serialization;
using FluentAssertions;
using Xunit;

namespace Birko.EventBus.Outbox.Tests;

/// <summary>
/// STORY-046 (EPIC-017): the outbox processor runs OUTSIDE the publishing request's async flow, so the
/// ambient scope (tenant) an event was published under is gone when it re-publishes. Under
/// TenantIsolationMode.Strict a tenant-scoped handler then throws. The processor must re-establish the
/// scope from the persisted <see cref="OutboxEntry.TenantGuid"/> via <see cref="IEventScopeAccessor"/>
/// before re-publishing. These tests use an AsyncLocal stand-in for the ambient tenant (no dependency on
/// Birko.Data.Tenant — proving the layering: the outbox restores scope through the abstraction only).
/// </summary>
public class OutboxScopeRestorationTests
{
    private const string TenantId = "11111111-1111-1111-1111-111111111111";

    private sealed record TenantThing : EventBase
    {
        public override string Source => "test";
    }

    /// <summary>AsyncLocal stand-in for an ambient ITenantContext.</summary>
    private static class AmbientTenant
    {
        private static readonly AsyncLocal<Guid?> _current = new();
        public static Guid? Current => _current.Value;

        public static IDisposable Enter(Guid? value)
        {
            var previous = _current.Value;
            _current.Value = value;
            return new Restore(previous);
        }

        private sealed class Restore(Guid? previous) : IDisposable
        {
            public void Dispose() => _current.Value = previous;
        }
    }

    /// <summary>Bridge under test's contract: restores the ambient tenant from EventContext.TenantGuid.</summary>
    private sealed class TenantRestoringScopeAccessor : IEventScopeAccessor
    {
        public async Task RunWithScopeAsync(EventContext context, Func<Task> body, CancellationToken cancellationToken = default)
        {
            using var _ = AmbientTenant.Enter(context.TenantGuid);
            await body();
        }
    }

    /// <summary>Inner bus stand-in: records the ambient tenant observed at (re-)publish time.</summary>
    private sealed class RecordingBus : IEventBus
    {
        public Guid? ObservedTenant;
        public int PublishCount;

        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : IEvent
        {
            ObservedTenant = AmbientTenant.Current;
            PublishCount++;
            return Task.CompletedTask;
        }

        public IEventSubscription Subscribe<TEvent>(IEventHandler<TEvent> handler) where TEvent : IEvent
            => throw new NotSupportedException();

        public void Dispose() { }
    }

    private static async Task<InMemoryOutboxStore> SeedAsync(Guid? tenant)
    {
        var serializer = new JsonMessageSerializer();
        var evt = new TenantThing();
        var store = new InMemoryOutboxStore();
        await store.SaveAsync(new OutboxEntry
        {
            EventId = evt.EventId,
            EventType = typeof(TenantThing).AssemblyQualifiedName!,
            Payload = serializer.Serialize(evt),
            Source = evt.Source,
            TenantGuid = tenant,
        });
        return store;
    }

    [Fact]
    public async Task Default_no_accessor_republishes_with_no_ambient_tenant()
    {
        // Documents the gap / preserved default: without a scope bridge, the processor's background flow
        // has no ambient tenant — which is exactly what throws under Strict once handlers touch a repo.
        var store = await SeedAsync(new Guid(TenantId));
        var bus = new RecordingBus();
        var processor = new OutboxProcessor(store, bus); // default = NullEventScopeAccessor

        var processed = await processor.ProcessBatchAsync();

        processed.Should().Be(1);
        bus.PublishCount.Should().Be(1);
        bus.ObservedTenant.Should().BeNull();
    }

    [Fact]
    public async Task Scope_accessor_restores_tenant_from_entry_before_republish()
    {
        // The fix: the processor wraps the re-publish in IEventScopeAccessor.RunWithScopeAsync with a
        // context carrying OutboxEntry.TenantGuid, so the ambient tenant is restored before dispatch.
        var tenant = new Guid(TenantId);
        var store = await SeedAsync(tenant);
        var bus = new RecordingBus();
        var processor = new OutboxProcessor(store, bus, scopeAccessor: new TenantRestoringScopeAccessor());

        var processed = await processor.ProcessBatchAsync();

        processed.Should().Be(1);
        bus.ObservedTenant.Should().Be(tenant,
            "the processor must re-establish the scope from the persisted OutboxEntry.TenantGuid");
    }

    [Fact]
    public async Task Scope_accessor_restores_null_scope_for_system_event()
    {
        // A null-tenant (system/cross-tenant) event restores a null scope — which a real tenant bridge
        // maps to WithAllTenants(...). The accessor still runs; it just carries no tenant.
        var store = await SeedAsync(tenant: null);
        var bus = new RecordingBus();
        var processor = new OutboxProcessor(store, bus, scopeAccessor: new TenantRestoringScopeAccessor());

        await processor.ProcessBatchAsync();

        bus.PublishCount.Should().Be(1);
        bus.ObservedTenant.Should().BeNull();
    }
}
