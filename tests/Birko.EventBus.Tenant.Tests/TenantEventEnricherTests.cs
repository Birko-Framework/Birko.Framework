using System;
using System.Threading.Tasks;
using Birko.Data.Tenant.Models;
using Birko.EventBus;
using Birko.EventBus.Enrichment;
using Birko.EventBus.Tenant;
using FluentAssertions;
using Xunit;

namespace Birko.EventBus.Tenant.Tests;

/// <summary>
/// STORY-046 (EPIC-017): the publish-side enricher stamps <see cref="EventContext.TenantGuid"/> from the
/// ambient tenant so <c>OutboxEntry.TenantGuid</c> is correct for every flow (HTTP, jobs, anonymous-with-
/// scope) — fixing the Guid.Empty-orphan bug for non-HTTP publishes under Strict.
/// </summary>
public class TenantEventEnricherTests
{
    private sealed record Thing : EventBase
    {
        public override string Source => "test";
    }

    private static readonly Guid TenantA = new("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Stamps_tenant_from_ambient_scope()
    {
        var ctx = new TenantContext();
        var enricher = new TenantEventEnricher(ctx);
        var eventContext = EventContext.From(new Thing());

        await ctx.WithTenantAsync(TenantA, null, () => enricher.EnrichAsync(new Thing(), eventContext));

        eventContext.TenantGuid.Should().Be(TenantA);
    }

    [Fact]
    public async Task Leaves_null_when_no_ambient_tenant()
    {
        var ctx = new TenantContext();
        var enricher = new TenantEventEnricher(ctx);
        var eventContext = EventContext.From(new Thing());

        await enricher.EnrichAsync(new Thing(), eventContext);

        eventContext.TenantGuid.Should().BeNull("no ambient tenant → genuine system / cross-tenant event");
    }

    [Fact]
    public async Task Nested_WithTenant_inside_WithAllTenants_captures_the_specific_tenant()
    {
        // The cross-tenant background-job attribution case: outer WithAllTenants (read all tenants),
        // inner WithTenant(entity.TenantGuid) around each per-entity publish. The inner tenant must win.
        var ctx = new TenantContext();
        var enricher = new TenantEventEnricher(ctx);
        var eventContext = EventContext.From(new Thing());

        await ctx.WithAllTenantsAsync(async () =>
        {
            await ctx.WithTenantAsync(TenantA, null, () => enricher.EnrichAsync(new Thing(), eventContext));
        });

        eventContext.TenantGuid.Should().Be(TenantA,
            "inner WithTenant wins for the enrich capture even inside an outer WithAllTenants");
    }

    [Fact]
    public async Task Does_not_overwrite_an_existing_value_with_null()
    {
        var ctx = new TenantContext();
        var enricher = new TenantEventEnricher(ctx);
        var eventContext = EventContext.From(new Thing(), TenantA); // pre-set by a prior enricher

        await enricher.EnrichAsync(new Thing(), eventContext); // no ambient tenant

        eventContext.TenantGuid.Should().Be(TenantA, "an already-attributed event must not be cleared");
    }
}
