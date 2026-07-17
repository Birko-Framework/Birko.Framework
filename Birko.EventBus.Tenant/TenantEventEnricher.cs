using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Tenant.Models;
using Birko.EventBus.Enrichment;

namespace Birko.EventBus.Tenant
{
    /// <summary>
    /// Publish-side counterpart to <see cref="TenantEventScopeAccessor"/>: stamps
    /// <see cref="EventContext.TenantGuid"/> from the ambient <see cref="ITenantContext"/> so the tenant an
    /// event was published under is captured (and persisted onto <c>OutboxEntry.TenantGuid</c>) regardless
    /// of entry point — HTTP request, background job, or an explicit <c>WithTenant</c> scope. STORY-046.
    /// </summary>
    /// <remarks>
    /// Reads the AsyncLocal-backed context (<see cref="Birko.Data.Tenant.Models.Tenant.Current"/>), which is
    /// populated in <b>every</b> flow at publish time — unlike an <c>HttpContext</c>-based enricher, which is
    /// null off the request thread and so leaves non-HTTP / anonymous publishes unattributed (the
    /// <c>Guid.Empty</c>-orphan bug under Strict). Enrichers run synchronously in the publishing flow, so
    /// nesting works: inside <c>WithTenant(t)</c> — even within an outer <c>WithAllTenants</c> —
    /// <see cref="ITenantContext.HasTenant"/> is true and the specific tenant is captured. When no tenant is
    /// in ambient scope the context is left null (a genuine system / cross-tenant event), which the consume
    /// side maps to <c>WithAllTenants</c>. Never overwrites an existing value with null.
    /// </remarks>
    public sealed class TenantEventEnricher : IEventEnricher
    {
        private readonly ITenantContext _tenantContext;

        /// <summary>Creates the enricher over the given tenant context.</summary>
        public TenantEventEnricher(ITenantContext tenantContext)
        {
            _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        }

        /// <inheritdoc />
        public Task EnrichAsync(IEvent @event, EventContext context, CancellationToken cancellationToken = default)
        {
            if (context is not null && _tenantContext.HasTenant)
            {
                context.TenantGuid = _tenantContext.CurrentTenantGuid;
            }

            return Task.CompletedTask;
        }
    }
}
