using System;
using Birko.Data.Tenant.Models;
using Birko.EventBus.Enrichment;
using Microsoft.Extensions.DependencyInjection;

namespace Birko.EventBus.Tenant
{
    /// <summary>
    /// DI registration for the event-bus ↔ tenant scope bridge (STORY-046, EPIC-017).
    /// </summary>
    public static class EventTenantScopeServiceCollectionExtensions
    {
        /// <summary>
        /// Registers <b>both</b> halves of the event ↔ tenant bridge over the process-wide AsyncLocal-backed
        /// <see cref="Birko.Data.Tenant.Models.Tenant.Current"/>.
        ///
        /// <para>
        /// ⚠ <b>SH-H053 — this overload is correct only when your application's <see cref="ITenantContext"/>
        /// IS <c>Tenant.Current</c>.</b> This doc used to claim it was "the same context
        /// <c>AddBirkoSecurity</c> / <c>AddTenantContext*</c> register", which is true of the first and
        /// <b>false</b> of the second:
        /// </para>
        /// <list type="bullet">
        /// <item><c>AddBirkoSecurity</c> registers <c>_ =&gt; Tenant.Current</c> — the same instance. ✔ Use
        /// this overload.</item>
        /// <item>Every <c>AddTenantContext*</c> overload registers <c>typeof(TenantContext)</c>, so the
        /// container constructs a <b>different</b> instance. <c>TenantContext</c> keeps its state in
        /// <b>instance</b> <c>AsyncLocal</c> fields, not static ones, so a second instance shares nothing:
        /// the request's tenant is set on the DI instance while this bridge reads <c>Tenant.Current</c> and
        /// sees <c>HasTenant == false</c>. ✘</item>
        /// </list>
        /// <para>
        /// <b>What that costs, because it is not a lost stamp but a widening.</b> The enricher leaves
        /// <c>EventContext.TenantGuid</c> null, and null is how a <i>genuine system event</i> is spelled —
        /// so <see cref="TenantEventScopeAccessor"/> dispatches the handler inside
        /// <c>WithAllTenantsAsync</c>. A tenant-scoped event therefore runs with
        /// <c>IsAllTenantsScope == true</c> and <c>Strict</c> repositories operate across every tenant. The
        /// two states are indistinguishable from the event alone, which is why this is documented rather
        /// than detected.
        /// </para>
        /// <para>
        /// ⚠ <b>And with <c>AddTenantContextScoped</c> / <c>AddTenantContextTransient</c> no overload of
        /// this method can work.</b> Both halves are registered as <b>singletons</b> (and consumed as
        /// singletons — <c>OutboxProcessor</c> takes <c>IEventScopeAccessor</c> from the root provider, and
        /// enrichers are <c>AddSingleton</c>), so there is no per-request instance for them to hold. That is
        /// a property of the bridge's lifetime, not of this wiring: use <c>AddBirkoSecurity</c>,
        /// <c>AddTenantContextSingleton</c>, or <c>Tenant.Current</c> directly.
        /// </para>
        ///
        /// The two halves:
        /// <list type="bullet">
        /// <item><b>Publish side</b> — <see cref="TenantEventEnricher"/> (an <see cref="IEventEnricher"/>) stamps
        /// <see cref="EventContext.TenantGuid"/> from the ambient tenant, so <c>OutboxEntry.TenantGuid</c> is
        /// correct for every flow (HTTP request, background job, explicit <c>WithTenant</c> scope). Consumers
        /// can drop hand-rolled <c>HttpContext</c>-based tenant enrichers.</item>
        /// <item><b>Consume side</b> — <see cref="TenantEventScopeAccessor"/> (an <see cref="IEventScopeAccessor"/>)
        /// restores that tenant before background dispatch, so handlers work under <c>TenantIsolationMode.Strict</c>.</item>
        /// </list>
        /// Call this alongside adopting Strict.
        /// </summary>
        public static IServiceCollection AddEventTenantScope(this IServiceCollection services)
            => services.AddEventTenantScope(Birko.Data.Tenant.Models.Tenant.Current);

        /// <summary>
        /// Registers the bridge over an explicit <see cref="ITenantContext"/>. Use this overload only when
        /// your tenant context is NOT the AsyncLocal-backed <see cref="Birko.Data.Tenant.Models.Tenant.Current"/>
        /// singleton; the supplied instance must be the same one both the publisher's ambient scope and the
        /// dispatched handlers' repositories observe.
        /// <para>
        /// ⚠ SH-H053: "the same one" is a real constraint, not a formality — see the parameterless
        /// overload's remarks. Because both halves are held as singletons, the instance supplied here must
        /// be one with process-wide lifetime; a scoped or transient <see cref="ITenantContext"/> cannot be
        /// made to work through this method at all, and passing one silently widens every tenant-scoped
        /// event to all tenants rather than failing.
        /// </para>
        /// </summary>
        public static IServiceCollection AddEventTenantScope(this IServiceCollection services, ITenantContext tenantContext)
        {
            if (services is null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            // Consume side: restore ambient scope from EventContext.TenantGuid before background dispatch.
            services.AddSingleton<IEventScopeAccessor>(new TenantEventScopeAccessor(tenantContext));
            // Publish side: capture the ambient tenant onto EventContext.TenantGuid so the outbox entry /
            // envelope is attributed correctly regardless of transport or entry point.
            services.AddSingleton<IEventEnricher>(new TenantEventEnricher(tenantContext));
            return services;
        }
    }
}
