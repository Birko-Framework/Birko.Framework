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
    Guid? CurrentTenantId { get; }

    /// <summary>
    /// The current tenant name (null if no tenant is set)
    /// </summary>
    string? CurrentTenantName { get; }

    /// <summary>
    /// Whether a tenant is currently set
    /// </summary>
    bool HasTenant { get; }

    /// <summary>
    /// Set the current tenant
    /// </summary>
    void SetTenant(Guid tenantId, string? tenantName = null);

    /// <summary>
    /// Clear the current tenant (switch to non-tenant mode)
    /// </summary>
    void ClearTenant();

    /// <summary>
    /// Execute an action within a specific tenant scope
    /// </summary>
    TResult? WithTenant<TResult>(Guid tenantId, string? tenantName, Func<TResult> action);

    /// <summary>
    /// Execute an async action within a specific tenant scope
    /// </summary>
    Task<TResult?> WithTenantAsync<TResult>(Guid tenantId, string? tenantName, Func<Task<TResult>> action);

    /// <summary>
    /// Execute an action within a specific tenant scope (no return value)
    /// </summary>
    void WithTenant(Guid tenantId, string? tenantName, Action action);

    /// <summary>
    /// Execute an async action within a specific tenant scope (no return value)
    /// </summary>
    Task WithTenantAsync(Guid tenantId, string? tenantName, Func<Task> action);
}
