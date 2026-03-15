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
    private const string TenantIdHeader = "X-Tenant-Id";
    private const string TenantNameHeader = "X-Tenant-Name";

    public Task<TenantInfo?> ResolveAsync(HttpContext context, CancellationToken ct = default)
    {
        var tenantIdStr = context.Request.Headers[TenantIdHeader].FirstOrDefault();
        if (tenantIdStr is null || !Guid.TryParse(tenantIdStr, out var tenantId))
            return Task.FromResult<TenantInfo?>(null);

        var tenantName = context.Request.Headers[TenantNameHeader].FirstOrDefault();
        return Task.FromResult<TenantInfo?>(new TenantInfo(tenantId, tenantName));
    }
}
