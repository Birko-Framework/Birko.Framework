using System;
using Birko.Security.Grants.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

// The same namespace as AddBirkoGrants, so a host that registered the grants already sees RequireGrant —
// in .Authorization it needed a using the README never showed (found by the TASK-590 smoke app).
namespace Birko.Security.Grants.AspNetCore;

/// <summary>Adds <see cref="RequireGrantEndpointFilter"/> to Minimal API endpoints.</summary>
public static class GrantEndpointExtensions
{
    /// <summary>
    /// Requires the caller to be allowed <paramref name="action"/> on the <paramref name="resourceType"/> resource whose id is
    /// the route value <paramref name="routeParam"/>; pass <c>null</c> for a type-wide check (e.g. "create").
    /// Validated against the registered <see cref="GrantRoleTable"/> when the endpoint is built.
    /// </summary>
    public static TBuilder RequireGrant<TBuilder>(this TBuilder builder, string resourceType, string action, string? routeParam = "id")
        where TBuilder : IEndpointConventionBuilder
    {
        if (resourceType == null) throw new ArgumentNullException(nameof(resourceType));
        if (action == null) throw new ArgumentNullException(nameof(action));

        if (routeParam != null)
        {
            // An optional or missing parameter would otherwise surface as a 500 any caller could trigger.
            builder.Add(endpoint =>
            {
                if (endpoint is not RouteEndpointBuilder route) return;
                var parameter = route.RoutePattern.GetParameter(routeParam);
                if (parameter == null || parameter.IsOptional)
                    throw new InvalidOperationException(
                        $"RequireGrant(\"{resourceType}\", \"{action}\") reads the route value '{routeParam}', but the route '{route.RoutePattern.RawText}' "
                        + (parameter == null ? "has no such parameter." : "makes it optional."));
            });
        }

        return builder.AddEndpointFilterFactory((factoryContext, next) =>
        {
            var services = factoryContext.ApplicationServices;
            var table = services.GetService<GrantRoleTable>()
                ?? throw new InvalidOperationException("RequireGrant needs AddBirkoGrants(...): no GrantRoleTable is registered.");
            var options = services.GetService<GrantsAspNetCoreOptions>() ?? new GrantsAspNetCoreOptions();
            var filter = new RequireGrantEndpointFilter(table, options, resourceType, action, routeParam);
            return invocation => filter.InvokeAsync(invocation, next);
        });
    }
}
