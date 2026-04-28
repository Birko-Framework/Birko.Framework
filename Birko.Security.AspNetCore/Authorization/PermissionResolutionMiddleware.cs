using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Birko.Security.AspNetCore.Authorization;

/// <summary>
/// After authentication, resolves the user's effective permissions via
/// <see cref="IUserPermissionResolver"/> and stashes the resulting set in
/// <see cref="HttpContext.Items"/> under <see cref="ItemsKey"/>.
///
/// <see cref="ResolvedPermissionsCurrentUser"/> reads from that slot so that
/// downstream <c>ICurrentUser.Permissions</c> consumers see them synchronously
/// without a per-call DB hit.
///
/// Pipeline order: register AFTER <c>UseAuthentication</c> (so identity claims
/// are populated) and BEFORE any handler that reads permissions.
/// </summary>
public sealed class PermissionResolutionMiddleware
{
    /// <summary>Key used to store the resolved permission set in <see cref="HttpContext.Items"/>.</summary>
    public const string ItemsKey = "birko:resolved-permissions";

    private readonly RequestDelegate _next;

    public PermissionResolutionMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, IUserPermissionResolver resolver, ClaimMappingOptions claimOptions)
    {
        if (context.User?.Identity?.IsAuthenticated == true)
        {
            var userIdClaim = context.User.FindFirstValue(claimOptions.UserIdClaim);
            if (Guid.TryParse(userIdClaim, out var userId))
            {
                var tenantClaim = context.User.FindFirstValue(claimOptions.TenantGuidClaim);
                Guid? tenantId = Guid.TryParse(tenantClaim, out var tid) && tid != Guid.Empty ? tid : null;

                var perms = await resolver.GetAsync(userId, tenantId, context.RequestAborted);
                context.Items[ItemsKey] = perms;
            }
        }

        await _next(context);
    }
}
