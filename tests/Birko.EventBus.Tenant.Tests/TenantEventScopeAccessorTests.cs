using System;
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

    [Fact]
    public async Task Empty_tenant_is_treated_as_system_event()
    {
        var tenantCtx = new TenantContext();
        var accessor = new TenantEventScopeAccessor(tenantCtx);

        var observedAllTenants = false;
        await accessor.RunWithScopeAsync(Ctx(Guid.Empty), () =>
        {
            observedAllTenants = tenantCtx.IsAllTenantsScope;
            return Task.CompletedTask;
        });

        observedAllTenants.Should().BeTrue("Guid.Empty is not a real tenant — treated as a system/cross-tenant event");
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
