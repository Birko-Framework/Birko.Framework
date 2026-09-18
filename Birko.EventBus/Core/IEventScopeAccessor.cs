using System;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.EventBus
{
    /// <summary>
    /// Re-establishes the ambient scope (tenant, correlation, …) an event was published under, for the
    /// duration of a background dispatch or re-publish. STORY-046 (EPIC-017).
    /// </summary>
    /// <remarks>
    /// Handlers that rely on ambient <c>AsyncLocal</c> context (e.g. the current tenant) break when
    /// dispatch happens OUTSIDE the publishing request's async flow — the outbox processor loop and the
    /// message-queue consumer callback both run tenant-less. The <see cref="EventContext"/> carries the
    /// original tenant (<see cref="EventContext.TenantGuid"/>), but nothing bridges it back onto the
    /// ambient scope before handlers run.
    /// <para>
    /// This hook is defined transport-agnostically in <c>Birko.EventBus</c> so the outbox / message-queue
    /// layers can restore scope <b>without</b> depending on any concrete ambient (e.g.
    /// <c>Birko.Data.Tenant</c>). The default <see cref="NullEventScopeAccessor"/> is a no-op (preserves
    /// pre-STORY-046 behaviour); a bridge package or the consumer supplies an implementation that maps
    /// <see cref="EventContext.TenantGuid"/> onto the tenant scope (e.g. <c>WithTenantAsync</c> when set,
    /// <c>WithAllTenants</c> when null).
    /// </para>
    /// </remarks>
    public interface IEventScopeAccessor
    {
        /// <summary>
        /// Runs <paramref name="body"/> within the ambient scope reconstructed from
        /// <paramref name="context"/>, restoring the previous scope afterwards.
        /// </summary>
        Task RunWithScopeAsync(EventContext context, Func<Task> body, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// No-op <see cref="IEventScopeAccessor"/> — runs the body without establishing any scope. The
    /// default when no scope bridge is registered, so behaviour is unchanged until a consumer opts in.
    /// </summary>
    public sealed class NullEventScopeAccessor : IEventScopeAccessor
    {
        /// <summary>Shared instance.</summary>
        public static readonly NullEventScopeAccessor Instance = new();

        /// <inheritdoc />
        public Task RunWithScopeAsync(EventContext context, Func<Task> body, CancellationToken cancellationToken = default)
            => body();
    }
}
