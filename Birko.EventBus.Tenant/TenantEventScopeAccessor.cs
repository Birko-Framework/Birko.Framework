using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Tenant.Models;

namespace Birko.EventBus.Tenant
{
    /// <summary>
    /// Bridges Birko.EventBus's <see cref="IEventScopeAccessor"/> to the Birko.Data.Tenant ambient tenant
    /// scope. Re-establishes the tenant an event was published under before background dispatch (outbox
    /// processor / message-queue consumer), so tenant-scoped handlers work under
    /// <c>TenantIsolationMode.Strict</c>. STORY-046 (EPIC-017).
    /// </summary>
    /// <remarks>
    /// Maps <see cref="EventContext.TenantGuid"/> onto the tenant scope, mirroring how the STORY-044
    /// background jobs opt into explicit cross-tenant access:
    /// <list type="bullet">
    /// <item>set (non-empty) → runs the body inside <see cref="ITenantContext.WithTenantAsync(Guid, string?, Func{Task})"/>;</item>
    /// <item>null / <see cref="Guid.Empty"/> → runs inside <see cref="ITenantContext.WithAllTenantsAsync(Func{Task})"/> (system / cross-tenant event).</item>
    /// </list>
    /// Assumes the supplied <see cref="ITenantContext"/> is the AsyncLocal-backed context the handlers'
    /// repositories also observe (as <c>AddBirkoSecurity</c> / <c>AddTenantContext*</c> register it) — the
    /// per-flow AsyncLocal state set here is what the dispatched handlers read.
    /// </remarks>
    public sealed class TenantEventScopeAccessor : IEventScopeAccessor
    {
        private readonly ITenantContext _tenantContext;

        /// <summary>
        /// Creates the bridge over the given tenant context.
        /// </summary>
        public TenantEventScopeAccessor(ITenantContext tenantContext)
        {
            _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        }

        /// <inheritdoc />
        public Task RunWithScopeAsync(EventContext context, Func<Task> body, CancellationToken cancellationToken = default)
        {
            if (body is null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            if (context?.TenantGuid is Guid tenant && tenant != Guid.Empty)
            {
                return _tenantContext.WithTenantAsync(tenant, null, body);
            }

            // No tenant on the event → deliberate cross-tenant (system) dispatch.
            return _tenantContext.WithAllTenantsAsync(body);
        }
    }
}
