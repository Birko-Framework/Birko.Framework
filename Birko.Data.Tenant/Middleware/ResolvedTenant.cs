using System;
using Microsoft.AspNetCore.Http;

namespace Birko.Data.Tenant.Middleware;

/// <summary>
/// The tenant a request's resolution chain actually produced, together with the door it came through.
/// Published on <see cref="HttpContext.Items"/> by every tenant-resolving middleware so that a
/// post-authentication step can correlate it with the caller's token (SH-H048).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why <see cref="HttpContext.Items"/> and not <c>ITenantContext</c>.</b> Both resolution stacks funnel
/// into an <c>ITenantContext</c>, so reading the tenant there would cover every source — but it can fail
/// <i>open</i> on a DI lifetime mismatch. <c>UseTenantMiddleware</c> captures its <c>ITenantContext</c> from
/// <c>ApplicationServices</c> at startup, while a downstream middleware resolves one from
/// <c>RequestServices</c>; under <c>AddTenantContextScoped()</c> those are two different objects, so the
/// guard would observe no tenant and wave the request through. Silently. That is the exact failure class
/// SH-H048 is about, so the resolution is carried per-request instead, where no registration lifetime can
/// come between the two halves.
/// </para>
/// <para>
/// The key is a fixed constant on purpose. <c>TenantMiddlewareOptions.TenantContextKey</c> is configurable,
/// and a guard that reads a configurable key is defeated by the same configuration change that defeated the
/// hard-coded <c>X-Tenant-Id</c> constant this replaces.
/// </para>
/// </remarks>
/// <param name="TenantGuid">The tenant the request resolved to.</param>
/// <param name="Source">
/// Human-readable description of the source, used in the 403 body so an operator can tell which door was
/// used. Must not contain quotes or backslashes — it is embedded in a hand-written JSON response.
/// </param>
public sealed record ResolvedTenant(Guid TenantGuid, string Source)
{
    /// <summary>
    /// Fixed <see cref="HttpContext.Items"/> key under which the resolution is published.
    /// </summary>
    public const string HttpContextItemKey = "Birko.Tenant.Resolved";

    /// <summary>
    /// Record the tenant this request resolved to, so post-authentication middleware can correlate it.
    /// </summary>
    public static void Publish(HttpContext context, Guid tenantGuid, string source)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));

        context.Items[HttpContextItemKey] = new ResolvedTenant(tenantGuid, source);
    }

    /// <summary>
    /// Read the tenant this request resolved to, or null when nothing resolved one.
    /// </summary>
    public static ResolvedTenant? From(HttpContext context)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));

        return context.Items.TryGetValue(HttpContextItemKey, out var value)
            ? value as ResolvedTenant
            : null;
    }
}
