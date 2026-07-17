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
    private readonly AsyncLocal<bool> _allTenantsScope = new();

    /// <inheritdoc />
    public Guid? CurrentTenantGuid => _currentTenantGuid.Value;

    /// <inheritdoc />
    public string? CurrentTenantName => _currentTenantName.Value;

    /// <inheritdoc />
    public bool HasTenant => _currentTenantGuid.Value.HasValue;

    /// <inheritdoc />
    public bool IsAllTenantsScope => _allTenantsScope.Value;

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

    /// <inheritdoc />
    public void WithAllTenants(Action action)
    {
        var previous = _allTenantsScope.Value;
        try
        {
            _allTenantsScope.Value = true;
            action();
        }
        finally
        {
            _allTenantsScope.Value = previous;
        }
    }

    /// <inheritdoc />
    public TResult? WithAllTenants<TResult>(Func<TResult> action)
    {
        var previous = _allTenantsScope.Value;
        try
        {
            _allTenantsScope.Value = true;
            return action();
        }
        finally
        {
            _allTenantsScope.Value = previous;
        }
    }

    /// <inheritdoc />
    public async Task WithAllTenantsAsync(Func<Task> action)
    {
        var previous = _allTenantsScope.Value;
        try
        {
            _allTenantsScope.Value = true;
            await action();
        }
        finally
        {
            _allTenantsScope.Value = previous;
        }
    }

    /// <inheritdoc />
    public async Task<TResult?> WithAllTenantsAsync<TResult>(Func<Task<TResult>> action)
    {
        var previous = _allTenantsScope.Value;
        try
        {
            _allTenantsScope.Value = true;
            return await action();
        }
        finally
        {
            _allTenantsScope.Value = previous;
        }
    }
}

/// <summary>
/// Process-wide singleton <see cref="ITenantContext"/> for <b>non-DI scenarios only</b> — console
/// apps, background tools, tests, and code that has no access to a DI container.
/// </summary>
/// <remarks>
/// CR-M175 footgun: in an ASP.NET (or any DI) app you must resolve <see cref="ITenantContext"/> from
/// the container (e.g. via <c>AddTenantContextScoped</c>) so that <c>TenantMiddleware</c> and every
/// store/repository observe the <i>same</i> per-request instance. If a store or repository is
/// accidentally constructed without an <see cref="ITenantContext"/>, the wrapper/factory falls back to
/// this static <see cref="Current"/> — a <i>different</i> instance whose tenant the middleware never set —
/// silently producing non-tenant-mode (unfiltered, cross-tenant) access. Do <b>not</b> mix the two:
/// in a DI app, always supply the scoped context and never read <see cref="Current"/>.
/// </remarks>
public static class Tenant
{
    private static readonly ITenantContext _instance = new TenantContext();

    /// <summary>
    /// Gets the process-wide tenant context. See the <see cref="Tenant"/> remarks: use this only in
    /// non-DI scenarios — in a DI app resolve a scoped <see cref="ITenantContext"/> instead, or
    /// tenant filtering silently degrades to unfiltered access.
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
