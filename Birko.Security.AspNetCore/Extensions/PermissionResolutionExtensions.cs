using System.Linq;
using Birko.Security.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Birko.Security.AspNetCore;

/// <summary>
/// Wiring for the per-request permission resolution path
/// (<see cref="IUserPermissionResolver"/> + <see cref="PermissionResolutionMiddleware"/> +
/// <see cref="ResolvedPermissionsCurrentUser"/>).
/// </summary>
public static class PermissionResolutionExtensions
{
    /// <summary>
    /// Switches <see cref="ICurrentUser"/> from the default claim-based reader
    /// (<see cref="ClaimsCurrentUser"/>) to <see cref="ResolvedPermissionsCurrentUser"/>,
    /// which expects permissions to be supplied per-request via
    /// <see cref="PermissionResolutionMiddleware"/>.
    ///
    /// The concrete <see cref="IUserPermissionResolver"/> implementation must be registered
    /// separately by the application (e.g.
    /// <c>services.AddScoped&lt;IUserPermissionResolver, MyResolver&gt;()</c>).
    /// </summary>
    /// <remarks>
    /// SH-M255: order-independent. It removes EVERY <see cref="ICurrentUser"/> registration, and
    /// <c>AddBirkoSecurity</c> registers <see cref="ClaimsCurrentUser"/> only when none exists. It removed only the
    /// first, and AddBirkoSecurity added unconditionally, so calling this first left ClaimsCurrentUser registered last
    /// and winning: permissions read from the token, the wider grant, with nothing at startup to say so.
    /// </remarks>
    public static IServiceCollection UseResolvedPermissions(this IServiceCollection services)
    {
        foreach (var existing in services.Where(sd => sd.ServiceType == typeof(ICurrentUser)).ToList())
            services.Remove(existing);
        services.AddScoped<ICurrentUser, ResolvedPermissionsCurrentUser>();
        return services;
    }

    /// <summary>
    /// Adds the resolution middleware. Must run AFTER <c>UseAuthentication</c> so that
    /// identity claims are populated, and BEFORE any handler that reads permissions.
    /// </summary>
    public static IApplicationBuilder UseBirkoPermissionResolution(this IApplicationBuilder app)
        => app.UseMiddleware<PermissionResolutionMiddleware>();
}
