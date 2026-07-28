using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Birko.Security.AspNetCore;

/// <summary>
/// Rejects a request whose <c>X-Tenant-Id</c> header names a tenant other than the one the caller's JWT was
/// issued for.
///
/// <para>
/// <see cref="HeaderTenantResolver"/> deliberately does nothing but parse the header — it runs from
/// <c>TenantMiddleware</c>, which sits <b>before</b> <c>UseAuthentication()</c>, so at that point
/// <c>context.User</c> is unpopulated and there is no claim to compare against. The correlation therefore
/// cannot live in the resolver; it has to be a separate step after authentication. That is this middleware.
/// </para>
/// <para>
/// Why it matters: in a typical app the header and the claim feed <i>different</i> consumers — repository
/// tenant scoping follows the header, while permission resolution follows the token. Unchecked, a caller
/// authenticates in their own tenant, sends <c>X-Tenant-Id: {victim}</c>, keeps their home-tenant
/// permissions (the token is untouched, so permission checks still pass) and points every tenant-scoped read
/// <b>and write</b> at another tenant. Writes are the serious half.
/// </para>
/// <para>
/// Enabled by default via <see cref="BirkoSecurityOptions.RequireTenantHeaderMatchesClaim"/>. Requests with
/// no header, unauthenticated requests, wildcard (<c>*</c>) holders and unparseable headers all pass
/// through — see <see cref="InvokeAsync"/> for why each is safe.
/// </para>
/// </summary>
public sealed class TenantHeaderClaimGuardMiddleware
{
    private const string TenantGuidHeader = "X-Tenant-Id";

    private readonly RequestDelegate _next;
    private readonly BirkoSecurityOptions _options;

    public TenantHeaderClaimGuardMiddleware(RequestDelegate next, BirkoSecurityOptions options)
    {
        _next = next;
        _options = options;
    }

    /// <summary>
    /// Compares header to claim and short-circuits with 403 on disagreement.
    /// </summary>
    public async Task InvokeAsync(HttpContext context, ICurrentUser currentUser)
    {
        if (!_options.RequireTenantHeaderMatchesClaim)
        {
            await _next(context);
            return;
        }

        // No header → the claim is the only tenant source, which is the correct path. Server-sent events in
        // particular cannot set headers, so this must keep working.
        var headerValue = context.Request.Headers[TenantGuidHeader].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(headerValue))
        {
            await _next(context);
            return;
        }

        // Unauthenticated requests carry no claim to compare against (login, register, first-run setup).
        // Those endpoints are anonymous by design and are not tenant-scoped by a header.
        if (!currentUser.IsAuthenticated)
        {
            await _next(context);
            return;
        }

        // A wildcard holder's cross-tenant reach is intentional — it may address any tenant.
        if (_options.WildcardPermissionEnabled && currentUser.Permissions.Contains("*"))
        {
            await _next(context);
            return;
        }

        // A malformed header resolves to no tenant in HeaderTenantResolver, so it cannot scope anything.
        // Treat it as absent rather than inventing a second failure mode for the same input.
        if (!Guid.TryParse(headerValue, out var headerTenant))
        {
            await _next(context);
            return;
        }

        var claimTenant = currentUser.TenantGuid ?? Guid.Empty;

        // Guid.Empty is the system/no-tenant scope. A non-wildcard caller whose token was issued for it must
        // not be able to name a real tenant via the header — that is the escalation this guard exists to stop.
        if (claimTenant != headerTenant)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(
                "{\"Error\":\"The X-Tenant-Id header does not match the tenant this session was issued for.\","
                + "\"Code\":\"Tenant.HeaderClaimMismatch\"}");
            return;
        }

        await _next(context);
    }
}
