using System;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Tenant.Models;

/// <summary>
/// Default implementation of ITenantContext using AsyncLocal for thread-safe tenant storage
/// </summary>
public class TenantContext : ITenantContext
{
    private readonly AsyncLocal<Guid?> _currentTenantGuid = new();
    private readonly AsyncLocal<string?> _currentTenantName = new();

    /// <inheritdoc />
    public Guid? CurrentTenantGuid => _currentTenantGuid.Value;

    /// <inheritdoc />
    public string? CurrentTenantName => _currentTenantName.Value;

    /// <inheritdoc />
    public bool HasTenant => _currentTenantGuid.Value.HasValue;

    /// <inheritdoc />
    public void SetTenant(Guid tenantGuid, string? tenantName = null)
    {
        _currentTenantGuid.Value = tenantGuid;
        _currentTenantName.Value = tenantName;
    }

    /// <inheritdoc />
    public void ClearTenant()
    {
        _currentTenantGuid.Value = null;
        _currentTenantName.Value = null;
    }

    /// <inheritdoc />
    public TResult? WithTenant<TResult>(Guid tenantGuid, string? tenantName, Func<TResult> action)
    {
        var previousTenantGuid = _currentTenantGuid.Value;
        var previousTenantName = _currentTenantName.Value;

        try
        {
            SetTenant(tenantGuid, tenantName);
            return action();
        }
        finally
        {
            _currentTenantGuid.Value = previousTenantGuid;
            _currentTenantName.Value = previousTenantName;
        }
    }

    /// <inheritdoc />
    public async Task<TResult?> WithTenantAsync<TResult>(Guid tenantGuid, string? tenantName, Func<Task<TResult>> action)
    {
        var previousTenantGuid = _currentTenantGuid.Value;
        var previousTenantName = _currentTenantName.Value;

        try
        {
            SetTenant(tenantGuid, tenantName);
            return await action();
        }
        finally
        {
            _currentTenantGuid.Value = previousTenantGuid;
            _currentTenantName.Value = previousTenantName;
        }
    }

    /// <inheritdoc />
    public void WithTenant(Guid tenantGuid, string? tenantName, Action action)
    {
        var previousTenantGuid = _currentTenantGuid.Value;
        var previousTenantName = _currentTenantName.Value;

        try
        {
            SetTenant(tenantGuid, tenantName);
            action();
        }
        finally
        {
            _currentTenantGuid.Value = previousTenantGuid;
            _currentTenantName.Value = previousTenantName;
        }
    }

    /// <inheritdoc />
    public async Task WithTenantAsync(Guid tenantGuid, string? tenantName, Func<Task> action)
    {
        var previousTenantGuid = _currentTenantGuid.Value;
        var previousTenantName = _currentTenantName.Value;

        try
        {
            SetTenant(tenantGuid, tenantName);
            await action();
        }
        finally
        {
            _currentTenantGuid.Value = previousTenantGuid;
            _currentTenantName.Value = previousTenantName;
        }
    }
}

/// <summary>
/// Singleton instance of TenantContext for application-wide use
/// </summary>
public static class Tenant
{
    private static readonly ITenantContext _instance = new TenantContext();

    /// <summary>
    /// Get the current tenant context instance
    /// </summary>
    public static ITenantContext Current => _instance;

    /// <summary>
    /// Set the current tenant
    /// </summary>
    public static void Set(Guid tenantGuid, string? tenantName = null)
        => _instance.SetTenant(tenantGuid, tenantName);

    /// <summary>
    /// Clear the current tenant
    /// </summary>
    public static void Clear()
        => _instance.ClearTenant();

    /// <summary>
    /// Get the current tenant ID
    /// </summary>
    public static Guid? Id => _instance.CurrentTenantGuid;

    /// <summary>
    /// Get the current tenant name
    /// </summary>
    public static string? Name => _instance.CurrentTenantName;

    /// <summary>
    /// Whether a tenant is currently set
    /// </summary>
    public static bool IsSet => _instance.HasTenant;
}
