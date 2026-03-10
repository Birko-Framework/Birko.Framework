using System;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Tenant.Models;

/// <summary>
/// Default implementation of ITenantContext using AsyncLocal for thread-safe tenant storage
/// </summary>
public class TenantContext : ITenantContext
{
    private readonly AsyncLocal<Guid?> _currentTenantId = new();
    private readonly AsyncLocal<string?> _currentTenantName = new();

    /// <inheritdoc />
    public Guid? CurrentTenantId => _currentTenantId.Value;

    /// <inheritdoc />
    public string? CurrentTenantName => _currentTenantName.Value;

    /// <inheritdoc />
    public bool HasTenant => _currentTenantId.Value.HasValue;

    /// <inheritdoc />
    public void SetTenant(Guid tenantId, string? tenantName = null)
    {
        _currentTenantId.Value = tenantId;
        _currentTenantName.Value = tenantName;
    }

    /// <inheritdoc />
    public void ClearTenant()
    {
        _currentTenantId.Value = null;
        _currentTenantName.Value = null;
    }

    /// <inheritdoc />
    public TResult? WithTenant<TResult>(Guid tenantId, string? tenantName, Func<TResult> action)
    {
        var previousTenantId = _currentTenantId.Value;
        var previousTenantName = _currentTenantName.Value;

        try
        {
            SetTenant(tenantId, tenantName);
            return action();
        }
        finally
        {
            _currentTenantId.Value = previousTenantId;
            _currentTenantName.Value = previousTenantName;
        }
    }

    /// <inheritdoc />
    public async Task<TResult?> WithTenantAsync<TResult>(Guid tenantId, string? tenantName, Func<Task<TResult>> action)
    {
        var previousTenantId = _currentTenantId.Value;
        var previousTenantName = _currentTenantName.Value;

        try
        {
            SetTenant(tenantId, tenantName);
            return await action();
        }
        finally
        {
            _currentTenantId.Value = previousTenantId;
            _currentTenantName.Value = previousTenantName;
        }
    }

    /// <inheritdoc />
    public void WithTenant(Guid tenantId, string? tenantName, Action action)
    {
        var previousTenantId = _currentTenantId.Value;
        var previousTenantName = _currentTenantName.Value;

        try
        {
            SetTenant(tenantId, tenantName);
            action();
        }
        finally
        {
            _currentTenantId.Value = previousTenantId;
            _currentTenantName.Value = previousTenantName;
        }
    }

    /// <inheritdoc />
    public async Task WithTenantAsync(Guid tenantId, string? tenantName, Func<Task> action)
    {
        var previousTenantId = _currentTenantId.Value;
        var previousTenantName = _currentTenantName.Value;

        try
        {
            SetTenant(tenantId, tenantName);
            await action();
        }
        finally
        {
            _currentTenantId.Value = previousTenantId;
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
    public static void Set(Guid tenantId, string? tenantName = null)
        => _instance.SetTenant(tenantId, tenantName);

    /// <summary>
    /// Clear the current tenant
    /// </summary>
    public static void Clear()
        => _instance.ClearTenant();

    /// <summary>
    /// Get the current tenant ID
    /// </summary>
    public static Guid? Id => _instance.CurrentTenantId;

    /// <summary>
    /// Get the current tenant name
    /// </summary>
    public static string? Name => _instance.CurrentTenantName;

    /// <summary>
    /// Whether a tenant is currently set
    /// </summary>
    public static bool IsSet => _instance.HasTenant;
}
