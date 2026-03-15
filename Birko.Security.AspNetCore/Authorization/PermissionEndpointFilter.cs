using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Birko.Security.AspNetCore;

/// <summary>
/// Minimal API endpoint filter that requires the caller to have a specific permission.
/// Usage: .AddEndpointFilter(new PermissionEndpointFilter("iot:device:write"))
/// </summary>
public sealed class PermissionEndpointFilter : IEndpointFilter
{
    private readonly string _requiredPermission;

    public PermissionEndpointFilter(string requiredPermission)
    {
        _requiredPermission = requiredPermission ?? throw new ArgumentNullException(nameof(requiredPermission));
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var currentUser = context.HttpContext.RequestServices.GetService(typeof(ICurrentUser)) as ICurrentUser;
        if (currentUser is null || !currentUser.IsAuthenticated)
            return Results.Json(new { error = "Unauthorized" }, statusCode: (int)HttpStatusCode.Unauthorized);

        if (!currentUser.Permissions.Contains(_requiredPermission) && !currentUser.Permissions.Contains("*"))
            return Results.Json(new { error = "Forbidden", required = _requiredPermission }, statusCode: (int)HttpStatusCode.Forbidden);

        return await next(context);
    }
}

/// <summary>
/// Extension methods for adding permission filters to endpoints.
/// </summary>
public static class PermissionEndpointExtensions
{
    /// <summary>
    /// Requires the caller to have the specified permission.
    /// </summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission)
        where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilter(new PermissionEndpointFilter(permission));
    }
}
