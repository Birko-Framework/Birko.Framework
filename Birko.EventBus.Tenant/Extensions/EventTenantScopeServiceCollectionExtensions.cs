using System;
using Birko.Data.Tenant.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Birko.EventBus.Tenant
{
    /// <summary>
    /// DI registration for the event-bus ↔ tenant scope bridge (STORY-046, EPIC-017).
    /// </summary>
    public static class EventTenantScopeServiceCollectionExtensions
    {
        /// <summary>
        /// Registers <see cref="TenantEventScopeAccessor"/> as the <see cref="IEventScopeAccessor"/>, over the
        /// process-wide AsyncLocal-backed <see cref="Birko.Data.Tenant.Models.Tenant.Current"/> — the same
        /// context <c>AddBirkoSecurity</c> / <c>AddTenantContext*</c> register as <see cref="ITenantContext"/>.
        /// The outbox processor (and any transport that consults the accessor) then restores the tenant an
        /// event was published under before dispatching handlers, so they work under
        /// <c>TenantIsolationMode.Strict</c>. Call this alongside adopting Strict.
        /// </summary>
        public static IServiceCollection AddEventTenantScope(this IServiceCollection services)
            => services.AddEventTenantScope(Birko.Data.Tenant.Models.Tenant.Current);

        /// <summary>
        /// Registers the bridge over an explicit <see cref="ITenantContext"/>. Use this overload only when
        /// your tenant context is NOT the AsyncLocal-backed <see cref="Birko.Data.Tenant.Models.Tenant.Current"/>
        /// singleton; the supplied instance must be the same one the dispatched handlers' repositories observe.
        /// </summary>
        public static IServiceCollection AddEventTenantScope(this IServiceCollection services, ITenantContext tenantContext)
        {
            if (services is null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            services.AddSingleton<IEventScopeAccessor>(new TenantEventScopeAccessor(tenantContext));
            return services;
        }
    }
}
