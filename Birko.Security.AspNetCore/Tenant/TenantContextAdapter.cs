using System;

namespace Birko.Security.AspNetCore;

/// <summary>
/// Adapts Birko.Data.Tenant.Models.ITenantContext to a simple ITenantContext
/// suitable for ASP.NET Core scoped DI. Delegates to Birko's AsyncLocal implementation.
/// </summary>
public sealed class TenantContextAdapter : ITenantContext
{
    private readonly Birko.Data.Tenant.Models.ITenantContext _birkoContext;

    public TenantContextAdapter(Birko.Data.Tenant.Models.ITenantContext birkoContext)
    {
        _birkoContext = birkoContext;
    }

    public Guid? CurrentTenantGuid => _birkoContext.CurrentTenantGuid;
    public string? CurrentTenantName => _birkoContext.CurrentTenantName;
    public bool HasTenant => _birkoContext.HasTenant;

    public void SetTenant(Guid tenantGuid, string? tenantName = null)
        => _birkoContext.SetTenant(tenantGuid, tenantName);

    public void ClearTenant()
        => _birkoContext.ClearTenant();
}

/// <summary>
/// Simple tenant context interface for ASP.NET Core applications.
/// Subset of Birko.Data.Tenant.Models.ITenantContext (without scoped execution methods).
/// </summary>
public interface ITenantContext
{
    Guid? CurrentTenantGuid { get; }
    string? CurrentTenantName { get; }
    bool HasTenant { get; }
    void SetTenant(Guid tenantGuid, string? tenantName = null);
    void ClearTenant();
}
