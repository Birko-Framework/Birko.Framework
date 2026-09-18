using System.Threading.Tasks;
using Birko.Data.Tenant.Middleware;
using Microsoft.AspNetCore.Http;

namespace Birko.Security.AspNetCore;

/// <summary>
/// ASP.NET Core middleware that resolves the current tenant and sets it in ITenantContext.
/// Clears the tenant after the request completes.
/// </summary>
public sealed class TenantMiddleware
{
    private readonly RequestDelegate _next;

    public TenantMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ITenantResolver resolver, ITenantContext tenantContext)
    {
        var tenant = await resolver.ResolveAsync(context, context.RequestAborted);
        if (tenant is not null)
        {
            tenantContext.SetTenant(tenant.TenantGuid, tenant.TenantName);

            // SH-H048: publish the resolution so TenantHeaderClaimGuardMiddleware can correlate it with the
            // token after authentication. Naming the resolver is what lets the guard cover a subdomain or
            // custom ITenantResolver it has no knowledge of — the gap that made the guard header-only.
            ResolvedTenant.Publish(context, tenant.TenantGuid, $"the tenant resolved by {resolver.GetType().Name}");
        }

        try
        {
            await _next(context);
        }
        finally
        {
            tenantContext.ClearTenant();
        }
    }
}
