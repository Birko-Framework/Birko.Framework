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
using Birko.Data.Tenant.Models;
using Birko.Data.Tenant.Stores;
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

    // ITenant + IDefault — STORY-045: the Default wrapper's uniqueness probe must be tenant-scoped,
    // which requires Tenant to sit INSIDE Default in the chain.
    public class TenantDefaultModel : AbstractModel, ITenant, IDefault
    {
        public Guid TenantGuid { get; set; }
        public string? TenantName { get; set; }
        public bool IsDefault { get; set; }
        public string? Note { get; set; }
    }

    // ITenant + ISluggable — STORY-045: the Sluggable wrapper's slug-collision probe must be
    // tenant-scoped, so the same slug is usable across tenants.
    public class TenantSluggableModel : AbstractModel, ITenant, ISluggable
    {
        public Guid TenantGuid { get; set; }
        public string? TenantName { get; set; }
        public string? Slug { get; set; }
        public string? Name { get; set; }
        public string? GetSlugSource() => Name;
    }

    // Plain ITenant — STORY-044: exercises the strict/permissive isolation mode end-to-end via Build.
    public class TenantOnlyModel : AbstractModel, ITenant
    {
        public Guid TenantGuid { get; set; }
        public string? TenantName { get; set; }
    }

    // ITenant + IEventSourced — STORY-045: the tenant guard must reject a cross-tenant write BEFORE the
    // EventSourcing wrapper records anything (Tenant stays OUTSIDE EventSourcing after the reorder).
    public class TenantEventSourcedModel : AbstractModel, ITenant, IEventSourced
    {
        private readonly List<IEvent> _uncommitted = new();
        public Guid TenantGuid { get; set; }
        public string? TenantName { get; set; }
        public long Version { get; set; }
        public void ApplyEvent(IEvent @event) => _uncommitted.Add(@event);
        public IEvent[] GetUncommittedEvents() => _uncommitted.ToArray();
        public void MarkEventsAsCommitted() => _uncommitted.Clear();
        public void LoadFromEvents(IEnumerable<IEvent> events) { }
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

    private sealed class RecordingAsyncEventStore : IAsyncEventStore
    {
        public int AppendCount { get; private set; }
        public Task AppendAsync(IEvent @event, CancellationToken cancellationToken = default) { AppendCount++; return Task.CompletedTask; }
        public Task AppendRangeAsync(IEnumerable<IEvent> events, CancellationToken cancellationToken = default) { AppendCount += events.Count(); return Task.CompletedTask; }
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

    [Fact]
    public async Task Default_uniqueness_is_scoped_per_tenant_not_global()
    {
        // STORY-045 regression: with Tenant outermost, AsyncDefaultStoreWrapper.UnsetOtherDefaultsAsync
        // probes+writes BELOW the tenant boundary, so tenant B setting its default silently clears
        // tenant A's default (cross-tenant data corruption). The fix moves Tenant inside Default so the
        // probe is tenant-scoped.
        var tenantA = new Guid("11111111-1111-1111-1111-111111111111");
        var tenantB = new Guid("22222222-2222-2222-2222-222222222222");

        var raw = Raw<TenantDefaultModel>();
        var ctx = new TenantContext();
        var store = StoreWrapperBuilder.Build<TenantDefaultModel>(raw, tenantContext: ctx);

        ctx.SetTenant(tenantA);
        await store.CreateAsync(new TenantDefaultModel { IsDefault = true });

        ctx.SetTenant(tenantB);
        await store.CreateAsync(new TenantDefaultModel { IsDefault = true });

        // Read straight from the raw (unscoped) store to inspect cross-tenant state.
        var all = await raw.ReadAsync(x => true);
        all.Single(x => x.TenantGuid == tenantA).IsDefault.Should().BeTrue(
            "tenant B setting its own default must not clear tenant A's default (per-tenant uniqueness)");
        all.Single(x => x.TenantGuid == tenantB).IsDefault.Should().BeTrue();
    }

    [Fact]
    public async Task Slug_uniqueness_is_scoped_per_tenant_not_global()
    {
        // STORY-045 regression: with Tenant outermost, AsyncSluggableBulkStoreWrapper's collision probe
        // reads BELOW the tenant boundary, so tenant B reusing tenant A's slug is forced to a
        // "-2" suffix (global uniqueness + cross-tenant existence leak). The fix moves Tenant inside
        // Sluggable so the probe is tenant-scoped and the same slug is usable per tenant.
        var tenantA = new Guid("11111111-1111-1111-1111-111111111111");
        var tenantB = new Guid("22222222-2222-2222-2222-222222222222");

        var raw = Raw<TenantSluggableModel>();
        var ctx = new TenantContext();
        var store = StoreWrapperBuilder.Build<TenantSluggableModel>(raw, tenantContext: ctx);

        ctx.SetTenant(tenantA);
        await store.CreateAsync(new TenantSluggableModel { Name = "Electronics" });

        ctx.SetTenant(tenantB);
        await store.CreateAsync(new TenantSluggableModel { Name = "Electronics" });

        var all = await raw.ReadAsync(x => true);
        all.Single(x => x.TenantGuid == tenantA).Slug.Should().Be("electronics");
        all.Single(x => x.TenantGuid == tenantB).Slug.Should().Be("electronics",
            "the same slug must be usable in a different tenant (per-tenant uniqueness), not forced to 'electronics-2'");
    }

    [Fact]
    public async Task Default_filter_update_only_touches_current_tenant_rows()
    {
        // STORY-045: AsyncDefaultStoreWrapper.UpdateAsync(filter, Action<T>) reads then writes via its
        // inner store; with Tenant inside Default those reads/writes are tenant-scoped, so a filter
        // update under tenant A must not mutate tenant B's rows.
        var tenantA = new Guid("11111111-1111-1111-1111-111111111111");
        var tenantB = new Guid("22222222-2222-2222-2222-222222222222");

        var raw = Raw<TenantDefaultModel>();
        var ctx = new TenantContext();
        var store = StoreWrapperBuilder.Build<TenantDefaultModel>(raw, tenantContext: ctx);

        ctx.SetTenant(tenantA);
        await store.CreateAsync(new TenantDefaultModel());
        ctx.SetTenant(tenantB);
        await store.CreateAsync(new TenantDefaultModel());

        // Acting as tenant A, blanket-update everything the filter matches.
        ctx.SetTenant(tenantA);
        await store.UpdateAsync(x => true, item => item.Note = "touched");

        var all = await raw.ReadAsync(x => true);
        all.Single(x => x.TenantGuid == tenantA).Note.Should().Be("touched");
        all.Single(x => x.TenantGuid == tenantB).Note.Should().BeNull(
            "a filter update scoped to tenant A must not mutate tenant B's rows");
    }

    [Fact]
    public async Task Tenant_guard_rejects_cross_tenant_write_before_any_event_is_recorded()
    {
        // STORY-045 invariant: Tenant stays OUTSIDE EventSourcing, so a cross-tenant guard rejection
        // throws before the EventSourcing wrapper records anything (no orphan events).
        var tenantA = new Guid("11111111-1111-1111-1111-111111111111");
        var tenantB = new Guid("22222222-2222-2222-2222-222222222222");

        var raw = Raw<TenantEventSourcedModel>();
        var ctx = new TenantContext();
        var events = new RecordingAsyncEventStore();
        var store = StoreWrapperBuilder.Build<TenantEventSourcedModel>(raw, tenantContext: ctx, eventStore: events);

        // An item owned by tenant B, but we act as tenant A.
        var foreignItem = new TenantEventSourcedModel { Guid = Guid.NewGuid(), TenantGuid = tenantB };
        ctx.SetTenant(tenantA);

        var act = async () => await store.UpdateAsync(foreignItem);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        events.AppendCount.Should().Be(0, "the tenant guard must reject before EventSourcing records an event");
    }

    // ---- STORY-044: opt-in strict (fail-closed) tenancy mode ----

    [Fact]
    public async Task Strict_mode_read_with_no_tenant_throws_instead_of_returning_all_tenants()
    {
        var raw = Raw<TenantOnlyModel>();
        var ctx = new TenantContext(); // no tenant set
        var store = StoreWrapperBuilder.Build<TenantOnlyModel>(raw, tenantContext: ctx, tenantMode: TenantIsolationMode.Strict);

        var act = async () => await store.ReadAsync(x => true);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Strict_mode_create_with_no_tenant_throws_instead_of_stamping_empty()
    {
        var raw = Raw<TenantOnlyModel>();
        var ctx = new TenantContext();
        var store = StoreWrapperBuilder.Build<TenantOnlyModel>(raw, tenantContext: ctx, tenantMode: TenantIsolationMode.Strict);

        var act = async () => await store.CreateAsync(new TenantOnlyModel());

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await raw.ReadAsync(x => true)).Should().BeEmpty("strict mode must not persist a Guid.Empty-stamped row");
    }

    [Fact]
    public async Task Strict_mode_filter_delete_with_no_tenant_throws()
    {
        var raw = Raw<TenantOnlyModel>();
        var ctx = new TenantContext();
        var store = StoreWrapperBuilder.Build<TenantOnlyModel>(raw, tenantContext: ctx, tenantMode: TenantIsolationMode.Strict);

        var act = async () => await store.DeleteAsync(x => true);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Permissive_mode_is_the_default_and_still_fails_open_on_reads()
    {
        // Backward-compat pin: Build without tenantMode keeps today's fail-open behaviour.
        var raw = Raw<TenantOnlyModel>();
        await raw.CreateAsync(new[]
        {
            new TenantOnlyModel { TenantGuid = new Guid("11111111-1111-1111-1111-111111111111") },
            new TenantOnlyModel { TenantGuid = new Guid("22222222-2222-2222-2222-222222222222") },
        });
        var ctx = new TenantContext(); // no tenant set
        var store = StoreWrapperBuilder.Build<TenantOnlyModel>(raw, tenantContext: ctx); // default Permissive

        var all = await store.ReadAsync(x => true);

        all.Should().HaveCount(2, "the permissive default still returns every tenant's rows when no tenant is set");
    }

    [Fact]
    public async Task Strict_mode_with_a_tenant_set_scopes_reads_to_that_tenant()
    {
        var tenantA = new Guid("11111111-1111-1111-1111-111111111111");
        var tenantB = new Guid("22222222-2222-2222-2222-222222222222");
        var raw = Raw<TenantOnlyModel>();
        await raw.CreateAsync(new[]
        {
            new TenantOnlyModel { TenantGuid = tenantA },
            new TenantOnlyModel { TenantGuid = tenantB },
        });
        var ctx = new TenantContext();
        var store = StoreWrapperBuilder.Build<TenantOnlyModel>(raw, tenantContext: ctx, tenantMode: TenantIsolationMode.Strict);

        ctx.SetTenant(tenantA);
        var rows = await store.ReadAsync(x => true);

        rows.Should().ContainSingle().Which.TenantGuid.Should().Be(tenantA);
    }

    [Fact]
    public async Task Strict_mode_all_tenants_scope_allows_deliberate_cross_tenant_read()
    {
        var tenantA = new Guid("11111111-1111-1111-1111-111111111111");
        var tenantB = new Guid("22222222-2222-2222-2222-222222222222");
        var raw = Raw<TenantOnlyModel>();
        await raw.CreateAsync(new[]
        {
            new TenantOnlyModel { TenantGuid = tenantA },
            new TenantOnlyModel { TenantGuid = tenantB },
        });
        var ctx = new TenantContext();
        var store = StoreWrapperBuilder.Build<TenantOnlyModel>(raw, tenantContext: ctx, tenantMode: TenantIsolationMode.Strict);

        // Outside a scope, strict fails closed.
        var blocked = async () => await store.ReadAsync(x => true);
        await blocked.Should().ThrowAsync<InvalidOperationException>();

        // Inside an explicit all-tenants scope, the same read is allowed and returns every tenant.
        var all = await ctx.WithAllTenantsAsync(async () => await store.ReadAsync(x => true));
        all.Should().HaveCount(2);
    }

    [Fact]
    public async Task Strict_mode_all_tenants_scope_preserves_per_item_tenant_on_create()
    {
        var tenantA = new Guid("11111111-1111-1111-1111-111111111111");
        var raw = Raw<TenantOnlyModel>();
        var ctx = new TenantContext();
        var store = StoreWrapperBuilder.Build<TenantOnlyModel>(raw, tenantContext: ctx, tenantMode: TenantIsolationMode.Strict);

        await ctx.WithAllTenantsAsync(async () =>
        {
            await store.CreateAsync(new TenantOnlyModel { TenantGuid = tenantA });
        });

        var all = await raw.ReadAsync(x => true);
        all.Should().ContainSingle().Which.TenantGuid.Should().Be(tenantA,
            "admin-scope create must preserve the caller's per-item TenantGuid, not stamp Guid.Empty");
    }

    [Fact]
    public async Task AsTenantAware_extension_honours_strict_mode()
    {
        var raw = Raw<TenantOnlyModel>();
        var ctx = new TenantContext();
        var store = ((IAsyncStore<TenantOnlyModel>)raw).AsTenantAware(ctx, TenantIsolationMode.Strict);

        var act = async () => await store.ReadAsync(x => true);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public void Sync_tenant_wrapper_honours_strict_mode()
    {
        // Sync-path parity (STORY-044 gap close): TenantStoreWrapper / TenantBulkStoreWrapper are
        // now mode-aware too, exercised through the sync AsTenantAware extension.
        var raw = new InMemoryStore<TenantOnlyModel>();
        var ctx = new TenantContext();
        var store = ((IStore<TenantOnlyModel>)raw).AsTenantAware(ctx, TenantIsolationMode.Strict);

        Action act = () => store.Read(x => true);

        act.Should().Throw<InvalidOperationException>();
    }
}
