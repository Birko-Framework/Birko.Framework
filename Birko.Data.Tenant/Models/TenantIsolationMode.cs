namespace Birko.Data.Tenant.Models;

/// <summary>
/// Controls how the tenant store wrappers behave when <b>no tenant is in scope</b>
/// (<see cref="ITenantContext.HasTenant"/> is false). See STORY-044 (EPIC-017).
/// </summary>
public enum TenantIsolationMode
{
    /// <summary>
    /// Fail-OPEN (default, backward-compatible). With no tenant set, reads/counts return every
    /// tenant's rows, filter-based writes affect every tenant, item writes are allowed ("admin
    /// mode"), and creates stamp <c>Guid.Empty</c>. Intended for back-office / maintenance flows.
    /// </summary>
    Permissive = 0,

    /// <summary>
    /// Fail-CLOSED. With no tenant set, every tenant-scoped operation throws instead of silently
    /// operating across all tenants: reads/counts/filter-writes throw
    /// <see cref="System.InvalidOperationException"/>, item writes throw
    /// (<see cref="System.UnauthorizedAccessException"/> via the belongs-to-tenant guard), and
    /// creates throw rather than stamping <c>Guid.Empty</c>. Cross-tenant/admin access must be an
    /// explicit, deliberate scope — never the accident of an unset context.
    /// </summary>
    Strict = 1,
}
