using System;
using Birko.Data.Tenant.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Birko.EventBus.Tenant
{
    /// <summary>
    /// TASK-328. Refuses an event ↔ tenant bridge that reads a different <see cref="ITenantContext"/> from the one the
    /// application's repositories resolve. In that wiring the enricher sees no tenant, an event published inside a
    /// tenant is stamped like a system event, and <see cref="TenantEventScopeAccessor"/> dispatches it across every
    /// tenant (SH-H053). The event alone cannot tell the two apart, but the container can.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The check is identity, not lifetime.</b> <c>AddBirkoSecurity</c> registers the context <i>scoped</i> as
    /// <c>_ =&gt; Tenant.Current</c>: scoped in name, one process-wide object in fact, and correct. A lifetime rule
    /// would refuse it. What breaks the bridge is a <i>different object</i>: <c>AddTenantContextScoped</c> /
    /// <c>Transient</c> (and <c>AddTenantContext()</c>, whose default is Scoped) build a new <c>TenantContext</c> per
    /// scope or per resolve, and <c>AddTenantContextSingleton</c> builds one that is not <c>Tenant.Current</c>.
    /// <c>TenantContext</c> keeps its state in instance <c>AsyncLocal</c> fields, so none of them share a tenant with
    /// the bridge.
    /// </para>
    /// <para>
    /// <b>When it runs.</b> Once, the first time the container builds either half of the bridge, so a wrong
    /// wiring fails before any event is enriched or dispatched. With the outbox hosted, that is host start: the
    /// processor resolves <c>IEventScopeAccessor</c> when it is built. One throwaway scope is created to resolve the
    /// context, then disposed. An application that registers no <see cref="ITenantContext"/> at all passes, because
    /// its code can only be using the one it gave the bridge.
    /// </para>
    /// </remarks>
    internal sealed class TenantContextWiringCheck
    {
        private readonly ITenantContext _bridgeContext;
        private readonly object _gate = new();
        private bool _verified;

        public TenantContextWiringCheck(ITenantContext bridgeContext)
        {
            _bridgeContext = bridgeContext;
        }

        public void Verify(IServiceProvider services)
        {
            if (_verified) return;
            lock (_gate)
            {
                if (_verified) return;

                ITenantContext? resolved;
                using (var scope = services.CreateScope())
                {
                    resolved = scope.ServiceProvider.GetService<ITenantContext>();
                }

                if (resolved != null && !ReferenceEquals(resolved, _bridgeContext))
                {
                    throw new InvalidOperationException(
                        "AddEventTenantScope reads a different ITenantContext from the one this application resolves "
                        + $"({resolved.GetType().FullName}, a separate instance). Every event published inside a tenant "
                        + "would be stamped as a system event and dispatched across ALL tenants. The bridge needs the "
                        + "process-wide context: register it as Tenant.Current (AddBirkoSecurity does, as "
                        + "`_ => Tenant.Current`), not through AddTenantContext / AddTenantContextScoped / "
                        + "AddTenantContextTransient / AddTenantContextSingleton, or pass the application's own process-wide "
                        + "instance to AddEventTenantScope(ITenantContext).");
                }

                _verified = true;
            }
        }
    }
}
