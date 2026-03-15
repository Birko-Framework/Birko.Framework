using System.Threading.Tasks;
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
