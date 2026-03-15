using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Birko.Security.AspNetCore;

/// <summary>
/// Resolves tenant from X-Tenant-Id and X-Tenant-Name HTTP headers.
/// </summary>
public sealed class HeaderTenantResolver : ITenantResolver
{
    private const string TenantGuidHeader = "X-Tenant-Id";
    private const string TenantNameHeader = "X-Tenant-Name";

    public Task<TenantInfo?> ResolveAsync(HttpContext context, CancellationToken ct = default)
    {
        var tenantGuidStr = context.Request.Headers[TenantGuidHeader].FirstOrDefault();
        if (tenantGuidStr is null || !Guid.TryParse(tenantGuidStr, out var tenantGuid))
            return Task.FromResult<TenantInfo?>(null);

        var tenantName = context.Request.Headers[TenantNameHeader].FirstOrDefault();
        return Task.FromResult<TenantInfo?>(new TenantInfo(tenantGuid, tenantName));
    }
}
