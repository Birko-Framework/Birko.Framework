using Microsoft.AspNetCore.Builder;

namespace Birko.Security.AspNetCore;

/// <summary>
/// Wiring for <see cref="TenantHeaderClaimGuardMiddleware"/>.
/// </summary>
public static class TenantHeaderGuardExtensions
{
    /// <summary>
    /// Rejects a request whose resolved tenant disagrees with the caller's JWT <c>tenant_id</c> claim
    /// (403 <c>Tenant.HeaderClaimMismatch</c>). Covers every tenant source, not just <c>X-Tenant-Id</c> —
    /// see <see cref="TenantHeaderClaimGuardMiddleware"/>. The method name is kept for compatibility.
    ///
    /// <para>
    /// Must run <b>after</b> <c>UseAuthentication()</c> — the claim is only populated there, which is why
    /// this cannot live in a resolver (those run from <c>TenantMiddleware</c>, before authentication) — and
    /// <b>before</b> anything that scopes by tenant, so every downstream consumer sees the same validated
    /// tenant:
    /// </para>
    /// <code>
    /// app.UseMiddleware&lt;TenantMiddleware&gt;();   // resolves the tenant
    /// app.UseAuthentication();
    /// app.UseAuthorization();
    /// app.UseBirkoTenantHeaderGuard();          // ← here
    /// // …tenant-scoped middleware and endpoints
    /// </code>
    /// <para>
    /// Behaviour is on by default; opt out with
    /// <see cref="BirkoSecurityOptions.RequireTenantHeaderMatchesClaim"/> = false, which re-opens
    /// cross-tenant addressing. Safe to call unconditionally — with the flag off the middleware is a
    /// pass-through.
    /// </para>
    /// </summary>
    public static IApplicationBuilder UseBirkoTenantHeaderGuard(this IApplicationBuilder app)
        => app.UseMiddleware<TenantHeaderClaimGuardMiddleware>();
}
