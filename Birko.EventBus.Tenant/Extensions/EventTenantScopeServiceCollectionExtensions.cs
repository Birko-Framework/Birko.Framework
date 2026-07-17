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
        /// <see cref="Birko.Data.Tenant.Models.Tenant.Current"/> — the same context <c>AddBirkoSecurity</c> /
        /// <c>AddTenantContext*</c> register as <see cref="ITenantContext"/>:
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
