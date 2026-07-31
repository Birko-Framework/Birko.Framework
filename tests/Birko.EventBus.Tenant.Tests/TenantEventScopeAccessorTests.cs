using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Birko.Data.Tenant.Models;
using Birko.EventBus;
using Birko.EventBus.Tenant;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Birko.EventBus.Tenant.Tests;

/// <summary>
/// STORY-046 (EPIC-017): the tenant bridge maps <see cref="EventContext.TenantGuid"/> onto the
/// Birko.Data.Tenant ambient scope so background event dispatch runs under the right tenant.
/// </summary>
public class TenantEventScopeAccessorTests
{
    private sealed record Thing : EventBase
    {
        public override string Source => "test";
    }

    private static EventContext Ctx(Guid? tenant) => EventContext.From(new Thing(), tenant);

    /// <summary>
    /// SH-H054. An outbox processor / MQ consumer draining events for many tenants plausibly runs the whole
    /// drain inside <c>WithAllTenants</c>, and this accessor then dispatches each tenant-scoped event
    /// through <c>WithTenantAsync</c> — producing the nested shape exactly. Before the fix the inner scope
    /// did not clear <c>IsAllTenantsScope</c>, so a tenant-scoped handler's reads still spanned every
    /// tenant while its writes were correctly narrowed.
    /// </summary>
    [Fact]
    public async Task Dispatch_inside_an_all_tenants_drain_still_narrows_to_the_events_tenant()
    {
        var tenantCtx = new TenantContext();
        var accessor = new TenantEventScopeAccessor(tenantCtx);
        var tenant = new Guid("22222222-2222-2222-2222-222222222222");

        Guid? observed = null;
        var observedAllTenants = true;

        await tenantCtx.WithAllTenantsAsync(() => accessor.RunWithScopeAsync(Ctx(tenant), () =>
        {
            observed = tenantCtx.CurrentTenantGuid;
            observedAllTenants = tenantCtx.IsAllTenantsScope;
            return Task.CompletedTask;
        }));

        observed.Should().Be(tenant);
        observedAllTenants.Should().BeFalse(
            "the event's own tenant is the innermost explicit scope and must win over the drain's admin scope");
    }

    /// <summary>
    /// The other half: the drain's all-tenants scope must survive the dispatch, or every event after the
    /// first would be handled under a stale narrow scope.
    /// </summary>
    [Fact]
    public async Task An_all_tenants_drain_is_restored_between_dispatches()
    {
        var tenantCtx = new TenantContext();
        var accessor = new TenantEventScopeAccessor(tenantCtx);
        var observedBetween = new List<bool>();

        await tenantCtx.WithAllTenantsAsync(async () =>
        {
            foreach (var t in new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() })
            {
                await accessor.RunWithScopeAsync(Ctx(t), () => Task.CompletedTask);
                observedBetween.Add(tenantCtx.IsAllTenantsScope);
            }
        });

        observedBetween.Should().Equal(true, true, true);
    }

    [Fact]
    public async Task Restores_specific_tenant_from_context()
    {
        var tenantCtx = new TenantContext();
        var accessor = new TenantEventScopeAccessor(tenantCtx);
        var tenant = new Guid("11111111-1111-1111-1111-111111111111");

        Guid? observed = null;
        var observedAllTenants = true;
        await accessor.RunWithScopeAsync(Ctx(tenant), () =>
        {
            observed = tenantCtx.CurrentTenantGuid;
            observedAllTenants = tenantCtx.IsAllTenantsScope;
            return Task.CompletedTask;
        });

        observed.Should().Be(tenant, "the body runs inside WithTenantAsync(context.TenantGuid)");
        observedAllTenants.Should().BeFalse("a tenant-scoped event is not an all-tenants scope");
        tenantCtx.HasTenant.Should().BeFalse("the scope is restored after the body completes");
    }

    [Fact]
    public async Task Null_tenant_runs_in_all_tenants_scope()
    {
        var tenantCtx = new TenantContext();
        var accessor = new TenantEventScopeAccessor(tenantCtx);

        var observedAllTenants = false;
        await accessor.RunWithScopeAsync(Ctx(tenant: null), () =>
        {
            observedAllTenants = tenantCtx.IsAllTenantsScope;
            return Task.CompletedTask;
        });

        observedAllTenants.Should().BeTrue("a null-tenant (system) event runs under WithAllTenants");
        tenantCtx.IsAllTenantsScope.Should().BeFalse("the scope is restored after the body completes");
    }

    /// <summary>
    /// Supersedes <c>Empty_tenant_is_treated_as_system_event</c>, which asserted the opposite
    /// (<c>IsAllTenantsScope == true</c>) on the reasoning that "Guid.Empty is not a real tenant".
    /// That folded a zero tenant into the <b>widening</b> branch: an event published inside a
    /// <c>Guid.Empty</c> scope was dispatched across every tenant. Symbio TASK-295 retired the
    /// "empty means unset" idiom across tenant scoping — <c>null</c> is the only "no tenant", and
    /// <see cref="EventContext.TenantGuid"/> is nullable end-to-end (outbox entry + MQ envelope), so a
    /// genuine system event still arrives as <c>null</c> and keeps the cross-tenant dispatch asserted by
    /// <see cref="Null_tenant_runs_in_all_tenants_scope"/>.
    /// </summary>
    [Fact]
    public async Task Empty_tenant_scopes_to_that_tenant_rather_than_widening_to_all()
    {
        var tenantCtx = new TenantContext();
        var accessor = new TenantEventScopeAccessor(tenantCtx);

        var observedAllTenants = true;
        Guid? observed = null;
        await accessor.RunWithScopeAsync(Ctx(Guid.Empty), () =>
        {
            observed = tenantCtx.CurrentTenantGuid;
            observedAllTenants = tenantCtx.IsAllTenantsScope;
            return Task.CompletedTask;
        });

        observedAllTenants.Should().BeFalse(
            "a zero tenant must not widen dispatch to every tenant — that is the fail-open direction");
        observed.Should().Be(Guid.Empty, "Guid.Empty is a tenant value; the body runs scoped to it");
    }

    [Fact]
    public void AddEventTenantScope_registers_both_bridge_halves()
    {
        var services = new ServiceCollection();

        services.AddEventTenantScope();

        var provider = services.BuildServiceProvider();
        provider.GetService<IEventScopeAccessor>().Should().BeOfType<TenantEventScopeAccessor>(
            "consume side — restores scope before dispatch");
        provider.GetService<Birko.EventBus.Enrichment.IEventEnricher>().Should().BeOfType<TenantEventEnricher>(
            "publish side — stamps EventContext.TenantGuid from the ambient tenant");
    }
}
