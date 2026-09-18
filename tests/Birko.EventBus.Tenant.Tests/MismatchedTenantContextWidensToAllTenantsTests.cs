using System;
using System.Threading.Tasks;
using Birko.Data.Tenant.Models;
using Birko.EventBus;
using Birko.EventBus.Tenant;
using FluentAssertions;
using Xunit;

namespace Birko.EventBus.Tenant.Tests;

/// <summary>
/// SH-H053 — <c>AddEventTenantScope()</c> binds <c>Tenant.Current</c>, which <c>AddTenantContext*</c> does
/// <b>not</b> register, so in that wiring a tenant-scoped event is dispatched across <b>every</b> tenant.
/// </summary>
/// <remarks>
/// <para>
/// <b>⚠ These are contract pins on the MECHANISM, not evidence of a fix.</b> The finding's remedy was a
/// documentation correction — the doc on <c>AddEventTenantScope</c> and on
/// <see cref="TenantEventScopeAccessor"/> both asserted that <c>AddBirkoSecurity</c> <i>and</i>
/// <c>AddTenantContext*</c> register the same context, which is true only of the first. Prose is not
/// testable, so what is pinned here is the load-bearing premise the corrected prose rests on: that two
/// <see cref="TenantContext"/> instances share nothing, and that the resulting chain terminates in an
/// all-tenants dispatch. Every test below passes before and after that correction.
/// </para>
/// <para>
/// <b>The chain, each link measured in <c>The_whole_chain_...</c> below.</b>
/// <c>TenantContext</c> keeps its state in <b>instance</b> <c>AsyncLocal</c> fields
/// (<c>private readonly AsyncLocal&lt;Guid?&gt; _currentTenantGuid = new()</c>), not static ones — so a
/// second instance is a second ambient scope. <c>AddTenantContext*</c> registers
/// <c>typeof(TenantContext)</c>, so the container builds one of those; <c>AddBirkoSecurity</c> instead
/// registers <c>_ =&gt; Tenant.Current</c>. Wire the bridge to one and set the tenant on the other and
/// <see cref="TenantEventEnricher"/> sees <c>HasTenant == false</c>, leaves
/// <see cref="EventContext.TenantGuid"/> null — and null is exactly how a <i>genuine system event</i> is
/// spelled, so <see cref="TenantEventScopeAccessor"/> widens to <c>WithAllTenantsAsync</c>. The two states
/// are indistinguishable from the event alone, which is why this is documented rather than detected.
/// </para>
/// <para>
/// It matters because <c>BelongsToCurrentTenant</c> deliberately fails open on <c>HasTenant == false</c>
/// (CR-L229, pinned by <c>TenantFailOpenTests</c>): under <c>Strict</c> the handler's repositories then
/// read and write every tenant's rows.
/// </para>
/// </remarks>
public class MismatchedTenantContextWidensToAllTenantsTests
{
    private sealed class Probe : IEvent
    {
        public Guid EventId { get; } = Guid.NewGuid();
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public string Source { get; } = nameof(MismatchedTenantContextWidensToAllTenantsTests);
    }

    // ---- the premise: instance AsyncLocal, so two instances are two ambient scopes ----

    [Fact]
    public void Two_TenantContext_instances_share_no_ambient_state()
    {
        // If these fields were STATIC AsyncLocals, instance identity would not matter and SH-H053 would
        // be a false positive. They are instance fields, which is what makes the whole finding real.
        var a = new TenantContext();
        var b = new TenantContext();
        var tenant = Guid.NewGuid();

        a.SetTenant(tenant, "A");

        a.HasTenant.Should().BeTrue();
        b.HasTenant.Should().BeFalse("state lives in instance AsyncLocal fields, not static ones");
        b.CurrentTenantGuid.Should().BeNull();
    }

    [Fact]
    public void Tenant_Current_is_not_the_instance_a_container_would_construct()
    {
        // AddTenantContext* registers typeof(TenantContext); AddBirkoSecurity registers _ => Tenant.Current.
        // This is the difference the corrected documentation turns on.
        var containerWouldBuild = new TenantContext();

        // Fully qualified: inside namespace Birko.EventBus.Tenant.Tests the bare name `Tenant`
        // binds to the NAMESPACE Birko.EventBus.Tenant, not to the tenant-context class.
        containerWouldBuild.Should().NotBeSameAs(Birko.Data.Tenant.Models.Tenant.Current);
    }

    // ---- the chain, end to end ----

    [Fact]
    public async Task The_whole_chain_widens_a_tenant_scoped_event_to_all_tenants_when_the_contexts_differ()
    {
        var bridgeContext = new TenantContext();       // what AddEventTenantScope() binds (Tenant.Current)
        var appContext = new TenantContext();          // what AddTenantContext* gives the request
        var enricher = new TenantEventEnricher(bridgeContext);
        var accessor = new TenantEventScopeAccessor(bridgeContext);
        var tenant = Guid.NewGuid();

        var context = new EventContext();
        await appContext.WithTenantAsync(tenant, "acme", async () =>
        {
            // Publish side: the request IS inside a tenant scope — on the app's context.
            appContext.HasTenant.Should().BeTrue("the request really is tenant-scoped");
            await enricher.EnrichAsync(new Probe(), context);
        });

        context.TenantGuid.Should().BeNull(
            "the enricher read the bridge's context, which this request never touched");

        // Consume side: a null TenantGuid is how a genuine system event is spelled, so it widens.
        bool sawAllTenants = false;
        Guid? sawTenant = null;
        await accessor.RunWithScopeAsync(context, () =>
        {
            sawAllTenants = bridgeContext.IsAllTenantsScope;
            sawTenant = bridgeContext.CurrentTenantGuid;
            return Task.CompletedTask;
        });

        sawAllTenants.Should().BeTrue(
            "this is the defect: a tenant-scoped event is dispatched across EVERY tenant, and under "
          + "Strict the handler's repositories follow it there");
        sawTenant.Should().BeNull("no specific tenant was ever restored");
    }

    [Fact]
    public async Task The_same_chain_is_correct_when_both_halves_share_one_context()
    {
        // The AddBirkoSecurity shape: bridge and app observe the same instance. Kept beside the failing
        // case so the difference is visibly the wiring and nothing else — the two tests are identical
        // apart from which context the request writes to.
        var shared = new TenantContext();
        var enricher = new TenantEventEnricher(shared);
        var accessor = new TenantEventScopeAccessor(shared);
        var tenant = Guid.NewGuid();

        var context = new EventContext();
        await shared.WithTenantAsync(tenant, "acme", async () =>
            await enricher.EnrichAsync(new Probe(), context));

        context.TenantGuid.Should().Be(tenant, "the bridge observed the tenant the request set");

        bool sawAllTenants = true;
        Guid? sawTenant = null;
        await accessor.RunWithScopeAsync(context, () =>
        {
            sawAllTenants = shared.IsAllTenantsScope;
            sawTenant = shared.CurrentTenantGuid;
            return Task.CompletedTask;
        });

        sawAllTenants.Should().BeFalse();
        sawTenant.Should().Be(tenant, "the handler runs scoped to the event's own tenant");
    }

    [Fact]
    public async Task A_genuine_system_event_still_widens_and_must_keep_doing_so()
    {
        // The reason SH-H053 cannot be fixed by detection: this is byte-identical to the defect case from
        // the event's point of view. Pinned so nobody "fixes" the widening and breaks cross-tenant
        // system events, which are the documented purpose of the null branch.
        var shared = new TenantContext();
        var enricher = new TenantEventEnricher(shared);
        var accessor = new TenantEventScopeAccessor(shared);

        var context = new EventContext();
        await enricher.EnrichAsync(new Probe(), context);   // no ambient tenant at all

        context.TenantGuid.Should().BeNull();

        bool sawAllTenants = false;
        await accessor.RunWithScopeAsync(context, () =>
        {
            sawAllTenants = shared.IsAllTenantsScope;
            return Task.CompletedTask;
        });

        sawAllTenants.Should().BeTrue(
            "a system event legitimately dispatches across tenants — which is precisely why the "
          + "mis-wired case is undetectable from the event and had to be documented instead");
    }
}
