using System;
using System.Threading.Tasks;

namespace Birko.Data.Tenant.Models;

/// <summary>
/// Interface for accessing the current tenant context
/// </summary>
public interface ITenantContext
{
    /// <summary>
    /// The current tenant ID (null if no tenant is set)
    /// </summary>
    Guid? CurrentTenantGuid { get; }

    /// <summary>
    /// The current tenant name (null if no tenant is set)
    /// </summary>
    string? CurrentTenantName { get; }

    /// <summary>
    /// Whether a tenant is currently set
    /// </summary>
    bool HasTenant { get; }

    /// <summary>
    /// Whether an explicit cross-tenant ("all tenants" / admin) scope is currently active. When
    /// true, <see cref="TenantIsolationMode.Strict"/> isolation is intentionally bypassed for the
    /// current async flow so back-office/maintenance code can operate across tenants on purpose.
    /// Default false; <see cref="TenantContext"/> backs this with AsyncLocal state (STORY-044).
    /// </summary>
    bool IsAllTenantsScope => false;

    /// <summary>
    /// Run an action within an explicit all-tenants (admin) scope. The default implementation runs
    /// the action WITHOUT establishing a scope — <see cref="TenantContext"/> overrides it to manage
    /// real AsyncLocal state. A custom <see cref="ITenantContext"/> that does not override this gets
    /// safe fail-closed behavior (no admin scope), so strict operations inside it still throw.
    /// </summary>
    void WithAllTenants(Action action) => action();

    /// <inheritdoc cref="WithAllTenants(Action)"/>
    TResult? WithAllTenants<TResult>(Func<TResult> action) => action();

    /// <inheritdoc cref="WithAllTenants(Action)"/>
    Task WithAllTenantsAsync(Func<Task> action) => action();

    /// <inheritdoc cref="WithAllTenants(Action)"/>
    async Task<TResult?> WithAllTenantsAsync<TResult>(Func<Task<TResult>> action) => await action();

    /// <summary>
    /// Set the current tenant
    /// </summary>
    void SetTenant(Guid tenantGuid, string? tenantName = null);

    /// <summary>
    /// Clear the current tenant (switch to non-tenant mode)
    /// </summary>
    void ClearTenant();

    /// <summary>
    /// Execute an action within a specific tenant scope.
    /// </summary>
    /// <remarks>
    /// <b>The innermost explicit scope wins (SH-H054).</b> An implementation must suspend any active
    /// all-tenants scope for the duration and restore it on exit, so
    /// <c>WithAllTenants(() =&gt; WithTenant(t, ... ))</c> — the per-tenant admin loop this pair exists for —
    /// actually narrows to <i>t</i> on each iteration instead of reading every tenant's rows every time.
    /// Restoring on exit is what keeps the next iteration from being left narrowed.
    /// </remarks>
    TResult? WithTenant<TResult>(Guid tenantGuid, string? tenantName, Func<TResult> action);

    /// <inheritdoc cref="WithTenant{TResult}(Guid, string?, Func{TResult})"/>
    Task<TResult?> WithTenantAsync<TResult>(Guid tenantGuid, string? tenantName, Func<Task<TResult>> action);

    /// <inheritdoc cref="WithTenant{TResult}(Guid, string?, Func{TResult})"/>
    void WithTenant(Guid tenantGuid, string? tenantName, Action action);

    /// <inheritdoc cref="WithTenant{TResult}(Guid, string?, Func{TResult})"/>
    Task WithTenantAsync(Guid tenantGuid, string? tenantName, Func<Task> action);
}
