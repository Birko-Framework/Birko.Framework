using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Birko.Security.AspNetCore;

/// <summary>
/// SH-M354 (TASK-161). <see cref="BirkoSecurityOptions.RequireTenantHeaderMatchesClaim"/> defaults to true, but only
/// <see cref="TenantHeaderClaimGuardMiddleware"/> reads it, and nothing put that middleware in the pipeline: an app that
/// adopted <c>AddBirkoSecurity</c> and never called <c>UseBirkoTenantHeaderGuard()</c> reported the guard on while
/// comparing nothing. The guard cannot be inserted for the app, because it must run after authentication, so the host
/// refuses to start instead. Opting out (<c>RequireTenantHeaderMatchesClaim = false</c>) starts as before.
/// </summary>
internal sealed class TenantHeaderGuardStartupCheck : IStartupFilter
{
    /// <summary>The builder property <c>UseBirkoTenantHeaderGuard</c> sets; read after the app's pipeline is built.</summary>
    internal const string Marker = "Birko.Security.TenantHeaderGuard";

    private readonly BirkoSecurityOptions _options;

    public TenantHeaderGuardStartupCheck(BirkoSecurityOptions options) => _options = options;

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        next(app);
        if (_options.RequireTenantHeaderMatchesClaim && !app.Properties.ContainsKey(Marker))
            throw new InvalidOperationException(
                "BirkoSecurityOptions.RequireTenantHeaderMatchesClaim is true, but the tenant header guard is not in the "
                + "pipeline, so a request's tenant header is never compared with its tenant claim. Call "
                + "app.UseBirkoTenantHeaderGuard() after UseAuthentication(), or set RequireTenantHeaderMatchesClaim = "
                + "false to opt out explicitly.");
    };
}
