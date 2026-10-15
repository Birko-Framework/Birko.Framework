using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
    /// <item>set → runs the body inside <see cref="ITenantContext.WithTenantAsync(Guid, string?, Func{Task})"/>,
    /// <b>including <see cref="Guid.Empty"/></b> — it is a tenant value, not "unset";</item>
    /// <item>null → runs inside <see cref="ITenantContext.WithAllTenantsAsync(Func{Task})"/> (system / cross-tenant event).</item>
    /// </list>
    /// <see cref="Guid.Empty"/> used to be folded into the null branch, so an event published inside a
    /// <c>Guid.Empty</c> tenant scope was <i>dispatched across every tenant</i> — the widening direction, and
    /// the same "empty means unset" idiom that made <c>ModelByTenant.Filter()</c> read every tenant's rows
    /// (Symbio TASK-295). <see cref="EventContext.TenantGuid"/> is nullable and stays nullable through the
    /// outbox and the message-queue envelope, so a genuine system event still arrives here as <c>null</c> and
    /// keeps its cross-tenant dispatch; only an explicitly-zero tenant now scopes instead of widening.
    /// Assumes the supplied <see cref="ITenantContext"/> is the AsyncLocal-backed context the handlers'
    /// repositories also observe — the per-flow AsyncLocal state set here is what the dispatched handlers
    /// read.
    /// <para>
    /// ⚠ <b>SH-H053:</b> this used to add "(as <c>AddBirkoSecurity</c> / <c>AddTenantContext*</c> register
    /// it)", which is false for the second. <c>AddTenantContext*</c> registers
    /// <c>typeof(TenantContext)</c> and that type holds its state in <b>instance</b> <c>AsyncLocal</c>
    /// fields, so the container's instance and <c>Tenant.Current</c> share nothing. When the assumption
    /// fails the enricher leaves <see cref="EventContext.TenantGuid"/> null, which is indistinguishable
    /// from a genuine system event — so the <c>null</c> branch below widens a tenant-scoped event to
    /// <b>all tenants</b>. That is the same widening direction as the <c>Guid.Empty</c> defect this class
    /// already records, reached by a different route: not a value being mis-read, but the bridge reading
    /// the wrong object. See <c>AddEventTenantScope</c>'s remarks for which registrations are safe.
    /// </para>
    /// </remarks>
    public sealed class TenantEventScopeAccessor : IEventScopeAccessor
    {
        private readonly ITenantContext _tenantContext;
        private readonly ConcurrentDictionary<string, AllTenantsDispatch> _allTenantsDispatches = new(StringComparer.Ordinal);
        private int _subscriberFailures;

        /// <summary>
        /// TASK-328. Every event source this bridge has dispatched across ALL tenants (a null
        /// <see cref="EventContext.TenantGuid"/>), with how often and when last. Current state, keyed by
        /// <see cref="EventContext.Source"/>, so it stays bounded by the number of event sources. A genuine system
        /// event belongs here; a source that should be tenant-scoped means its publish ran outside any tenant scope,
        /// or on a context the bridge cannot see. The wiring that causes the second is refused when the bridge is
        /// built (see <c>AddEventTenantScope</c>); this is what remains visible at dispatch.
        /// </summary>
        public IReadOnlyDictionary<string, AllTenantsDispatch> AllTenantsDispatches => _allTenantsDispatches;

        /// <summary>Raised for each all-tenants dispatch, before the handler runs. A throwing subscriber is counted, never propagated.</summary>
        public event Action<AllTenantsDispatch>? OnAllTenantsDispatch;

        /// <summary>How many times an <see cref="OnAllTenantsDispatch"/> subscriber has thrown. The dispatch is unaffected.</summary>
        public int SubscriberFailures => Volatile.Read(ref _subscriberFailures);

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

            // Guid.Empty is a tenant value, not "unset" — scope to it rather than widening to all tenants.
            if (context?.TenantGuid is Guid tenant)
            {
                return _tenantContext.WithTenantAsync(tenant, null, body);
            }

            // NO tenant on the event (null) → deliberate cross-tenant (system) dispatch.
            RecordAllTenantsDispatch(context);
            return _tenantContext.WithAllTenantsAsync(body);
        }

        private void RecordAllTenantsDispatch(EventContext? context)
        {
            var source = string.IsNullOrEmpty(context?.Source) ? "(unknown)" : context!.Source;
            var now = DateTime.UtcNow;
            var entry = _allTenantsDispatches.AddOrUpdate(
                source,
                _ => new AllTenantsDispatch(source, 1, now),
                (_, previous) => new AllTenantsDispatch(source, previous.Count + 1, now));

            var handlers = OnAllTenantsDispatch;
            if (handlers == null) return;
            foreach (Action<AllTenantsDispatch> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(entry);
                }
                catch
                {
                    // A diagnostic channel must survive its own subscriber (rule 25): a throwing handler here
                    // would fail the dispatch it is only meant to observe.
                    Interlocked.Increment(ref _subscriberFailures);
                }
            }
        }
    }

    /// <summary>One event source's all-tenants dispatches (TASK-328): how many, and when the last one ran (UTC).</summary>
    public sealed record AllTenantsDispatch(string Source, long Count, DateTime LastDispatchedAt);
}
