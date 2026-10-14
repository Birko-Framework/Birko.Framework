using System;
using System.Net;
using System.Threading.Tasks;
using Birko.Security.AspNetCore;
using Birko.Security.Grants.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Birko.Security.Grants.AspNetCore.Authorization;

/// <summary>
/// Minimal API filter requiring a per-resource grant: the caller may perform <c>action</c> on the
/// <c>resourceType</c> resource whose id is the route value <c>routeParam</c> (or on the type, when it is <c>null</c>).
/// </summary>
/// <remarks>
/// <para>Responses, in order:</para>
/// <list type="number">
/// <item>Not authenticated → <b>401</b>. Decided before any lookup, so it says nothing about any resource.</item>
/// <item>Authenticated but no local user Guid (the token's subject is the provider's raw id) → <b>401</b> with code
/// <c>Grant.UnmappedSubject</c>: the host must issue its own token whose subject is its user's Guid.</item>
/// <item>The route value does not parse as a Guid → <b>404</b>: no such resource can exist.</item>
/// <item>Denied on a <see cref="ResourceVisibility.Public"/> type → <b>403</b>. On a <see cref="ResourceVisibility.Granted"/>
/// type → 403 if the caller may perform <see cref="GrantsAspNetCoreOptions.ReadAction"/> on it, otherwise a bare <b>404</b>,
/// identical to a missing resource (decision 2b).</item>
/// </list>
/// <para>Everything checkable is checked when the endpoint is built (the first enumeration of the endpoint data source,
/// normally the first request), not per request: the resource type, the action and the read action against the
/// <see cref="GrantRoleTable"/>, and that the route pattern has a required parameter named <c>routeParam</c>. A request
/// still reaching the filter without the value throws.</para>
/// </remarks>
public sealed class RequireGrantEndpointFilter : IEndpointFilter
{
    /// <summary>Code carried by the 401 for an authenticated caller with no local user Guid.</summary>
    public const string UnmappedSubject = "Grant.UnmappedSubject";

    private readonly string _resourceType;
    private readonly string _action;
    private readonly string? _routeParam;
    private readonly string _readAction;
    private readonly ResourceVisibility _visibility;

    internal RequireGrantEndpointFilter(GrantRoleTable table, GrantsAspNetCoreOptions options, string resourceType, string action, string? routeParam)
    {
        _visibility = table.VisibilityOf(resourceType);
        table.RequireAction(action);
        table.RequireAction(options.ReadAction);
        _resourceType = resourceType;
        _action = action;
        _routeParam = routeParam;
        _readAction = options.ReadAction;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var user = http.RequestServices.GetService<ICurrentUser>();
        if (user is null || !user.IsAuthenticated)
            return Results.Json(new { error = "Unauthorized" }, statusCode: (int)HttpStatusCode.Unauthorized);
        if (user.UserGuid is not Guid userGuid)
            return Results.Json(new { error = "Unauthorized", code = UnmappedSubject }, statusCode: (int)HttpStatusCode.Unauthorized);

        Guid? resourceId = null;
        if (_routeParam != null)
        {
            if (!http.Request.RouteValues.TryGetValue(_routeParam, out var raw) || raw is null)
                throw new InvalidOperationException(
                    $"RequireGrant(\"{_resourceType}\", \"{_action}\") reads the route value '{_routeParam}', which this endpoint's route does not have.");
            if (!Guid.TryParse(raw.ToString(), out var parsed))
                return Results.NotFound();
            resourceId = parsed;
        }

        var grants = http.RequestServices.GetRequiredService<IGrantService>();
        var ct = http.RequestAborted;
        // Guid.Empty is "no tenant", exactly as PermissionResolutionMiddleware reads the claim, so the decorator and
        // this check always evaluate the same scope.
        var tenant = user.TenantGuid is Guid t && t != Guid.Empty ? t : (Guid?)null;
        if (await grants.CanAsync(userGuid, _resourceType, resourceId, _action, tenant, ct).ConfigureAwait(false))
            return await next(context).ConfigureAwait(false);

        if (_visibility == ResourceVisibility.Granted && resourceId != null
            && !await grants.CanAsync(userGuid, _resourceType, resourceId, _readAction, tenant, ct).ConfigureAwait(false))
            return Results.NotFound();

        return Results.Json(new { error = "Forbidden", required = $"{_resourceType}:{_action}" }, statusCode: (int)HttpStatusCode.Forbidden);
    }
}
