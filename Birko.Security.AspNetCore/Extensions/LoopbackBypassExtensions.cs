using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Birko.Security.AspNetCore;

/// <summary>Wiring for <see cref="LoopbackBypassMiddleware"/> (TASK-522).</summary>
public static class LoopbackBypassExtensions
{
    /// <summary>
    /// Lets an unauthenticated request through as a wildcard-permission principal when
    /// <see cref="BirkoSecurityOptions.LoopbackBypass"/> is on and every address the server bound is loopback. Must run
    /// <b>after</b> <c>UseAuthentication()</c> (a request that authenticated keeps its principal) and <b>before</b>
    /// <c>UseAuthorization()</c> (which is what the principal has to pass). A pass-through when the flag is off, so it
    /// is safe to call unconditionally.
    /// </summary>
    public static IApplicationBuilder UseBirkoLoopbackBypass(this IApplicationBuilder app)
    {
        app.Properties[LoopbackBypassStartupCheck.Marker] = true;
        return app.UseMiddleware<LoopbackBypassMiddleware>();
    }
}

/// <summary>
/// The host refuses to start when <see cref="BirkoSecurityOptions.LoopbackBypass"/> is on but
/// <see cref="LoopbackBypassExtensions.UseBirkoLoopbackBypass"/> is not in the pipeline: the flag would read as on and
/// do nothing (the same reasoning as <see cref="TenantHeaderGuardStartupCheck"/>).
/// </summary>
internal sealed class LoopbackBypassStartupCheck : IStartupFilter
{
    internal const string Marker = "Birko.Security.LoopbackBypass";

    private readonly BirkoSecurityOptions _options;

    public LoopbackBypassStartupCheck(BirkoSecurityOptions options) => _options = options;

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        next(app);
        if (_options.LoopbackBypass && !app.Properties.ContainsKey(Marker))
            throw new InvalidOperationException(
                "BirkoSecurityOptions.LoopbackBypass is true, but app.UseBirkoLoopbackBypass() is not in the pipeline, "
                + "so the bypass never applies. Call it after UseAuthentication() and before UseAuthorization(), or "
                + "turn the option off.");
    };
}
