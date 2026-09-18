using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Tenant.Middleware;
using Microsoft.AspNetCore.Http;

namespace Birko.Security.AspNetCore;

/// <summary>
/// Rejects a request that addresses a tenant other than the one the caller's JWT was issued for.
///
/// <para>
/// Tenant resolution runs from a <c>TenantMiddleware</c>, which sits <b>before</b>
/// <c>UseAuthentication()</c> — at that point <c>context.User</c> is unpopulated and there is no claim to
/// compare against. The correlation therefore cannot live in a resolver; it has to be a separate step after
/// authentication. That is this middleware.
/// </para>
/// <para>
/// Why it matters: in a typical app the resolved tenant and the claim feed <i>different</i> consumers —
/// repository tenant scoping follows the resolution, while permission resolution follows the token.
/// Unchecked, a caller authenticates in their own tenant, addresses <c>{victim}</c>, keeps their home-tenant
/// permissions (the token is untouched, so permission checks still pass) and points every tenant-scoped read
/// <b>and write</b> at another tenant. Writes are the serious half.
/// </para>
/// <para>
/// <b>The guard is on the resolved tenant, not on a transport (SH-H048).</b> It originally compared one
/// hard-coded <c>X-Tenant-Id</c> header, which left every other door open: a query-string key, a route value,
/// a subdomain, either custom-resolver hook, and — the quiet one — a <i>renamed</i>
/// <c>TenantMiddlewareOptions.TenantHeaderName</c>, which made the guard stop working with no error and no
/// warning on a deployment that looked correctly configured. Each resolving middleware now publishes its
/// result as a <see cref="ResolvedTenant"/> and this guard checks that, so a source added later is covered
/// without touching this file.
/// </para>
/// <para>
/// The literal <c>X-Tenant-Id</c> check is <b>retained on top</b> of the resolved-tenant check. It is not
/// redundant: an app that never wired a tenant middleware resolves nothing, yet its own code may still read
/// the header directly — which is the premise the original guard was written on. Dropping it would have made
/// this task a coverage regression for those apps.
/// </para>
/// <para>
/// Enabled by default via <see cref="BirkoSecurityOptions.RequireTenantHeaderMatchesClaim"/>. Requests that
/// address no tenant, unauthenticated requests, wildcard (<c>*</c>) holders and unparseable sources all pass
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
    /// Compares every tenant this request addresses to the caller's claim and short-circuits with 403 on the
    /// first disagreement.
    /// </summary>
    /// <remarks>
    /// <paramref name="tenantContext"/> is a required parameter rather than an optional lookup so that a
    /// missing registration fails the request loudly instead of degrading this guard to a pass-through.
    /// </remarks>
    public async Task InvokeAsync(HttpContext context, ICurrentUser currentUser, ITenantContext tenantContext)
    {
        if (!_options.RequireTenantHeaderMatchesClaim)
        {
            await _next(context);
            return;
        }

        // Addresses no tenant → the claim is the only tenant source, which is the correct path. Server-sent
        // events in particular cannot set headers, so this must keep working. An unparseable source lands
        // here too: it resolves to nothing, so it cannot scope anything, and treating it as absent avoids
        // inventing a second failure mode for the same input.
        var addressed = CollectAddressedTenants(context, tenantContext);
        if (addressed.Count == 0)
        {
            await _next(context);
            return;
        }

        // Unauthenticated requests carry no claim to compare against (login, register, first-run setup).
        // Those endpoints are anonymous by design and are not tenant-scoped.
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

        // Guid.Empty is the system/no-tenant scope. A non-wildcard caller whose token was issued for it must
        // not be able to name a real tenant — that is the escalation this guard exists to stop.
        var claimTenant = currentUser.TenantGuid ?? Guid.Empty;

        foreach (var (tenantGuid, source) in addressed)
        {
            if (tenantGuid == claimTenant)
                continue;

            await WriteMismatchAsync(context, source);
            return;
        }

        await _next(context);
    }

    /// <summary>
    /// Every tenant this request addresses, whichever door it came through, de-duplicated by tenant.
    /// </summary>
    private static List<(Guid TenantGuid, string Source)> CollectAddressedTenants(
        HttpContext context,
        ITenantContext tenantContext)
    {
        var addressed = new List<(Guid TenantGuid, string Source)>();

        void Add(Guid tenantGuid, string source)
        {
            if (addressed.Any(a => a.TenantGuid == tenantGuid))
                return;

            addressed.Add((tenantGuid, source));
        }

        // 1. What the resolution chain actually produced — the source-agnostic check, and the one that covers
        //    query string, route value, subdomain, custom resolvers and renamed headers alike.
        var resolved = ResolvedTenant.From(context);
        if (resolved is not null)
        {
            Add(resolved.TenantGuid, resolved.Source);
        }

        // 2. The ambient context, for a resolver that sets it without publishing (a consumer's own middleware
        //    predating ResolvedTenant). Belt and braces; costs nothing when 1 already covered it.
        if (tenantContext.HasTenant && tenantContext.CurrentTenantGuid.HasValue)
        {
            Add(tenantContext.CurrentTenantGuid.Value, "the tenant resolved for this request");
        }

        // 3. The literal header, even when nothing resolved from it — see the class remarks.
        var headerValue = context.Request.Headers[TenantGuidHeader].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(headerValue) && Guid.TryParse(headerValue, out var headerTenant))
        {
            Add(headerTenant, $"the {TenantGuidHeader} header");
        }

        return addressed;
    }

    private static async Task WriteMismatchAsync(HttpContext context, string source)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(
            $"{{\"Error\":\"{Sanitize(source)} does not match the tenant this session was issued for.\","
            + "\"Code\":\"Tenant.HeaderClaimMismatch\"}");
    }

    /// <summary>
    /// Source descriptions can embed consumer-configured key names, and the response body is hand-written
    /// JSON — strip anything that could break out of the string rather than escaping it.
    /// </summary>
    private static string Sanitize(string? source)
    {
        // Source strings come from ResolvedTenant.Publish, whose caller is a consumer's middleware — a null
        // or blank description must degrade to a generic phrase, not throw out of a security check.
        if (string.IsNullOrWhiteSpace(source))
            return Fallback;

        var cleaned = new string(source.Where(c => c != '"' && c != '\\' && !char.IsControl(c)).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? Fallback : Capitalize(cleaned);
    }

    private const string Fallback = "The tenant addressed by this request";

    private static string Capitalize(string value)
        => char.IsLower(value[0]) ? char.ToUpperInvariant(value[0]) + value.Substring(1) : value;
}
