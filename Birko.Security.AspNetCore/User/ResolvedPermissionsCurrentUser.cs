using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Birko.Security.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Birko.Security.AspNetCore;

/// <summary>
/// <see cref="ICurrentUser"/> variant that reads identity claims (UserId, Email, TenantGuid,
/// Roles) from <see cref="HttpContext.User"/> just like <see cref="ClaimsCurrentUser"/>,
/// but pulls <see cref="Permissions"/> from <see cref="HttpContext.Items"/> populated by
/// <see cref="PermissionResolutionMiddleware"/>.
///
/// Use this when permissions are too large to embed in the JWT and must be resolved
/// server-side per request. Falls back to an empty set if the middleware has not run
/// (e.g. unauthenticated paths, or a misconfigured pipeline).
/// </summary>
public sealed class ResolvedPermissionsCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ClaimMappingOptions _options;

    public ResolvedPermissionsCurrentUser(IHttpContextAccessor httpContextAccessor, ClaimMappingOptions options)
    {
        _httpContextAccessor = httpContextAccessor;
        _options = options;
    }

    private ClaimsPrincipal? Principal => _httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public Guid? UserId
    {
        get
        {
            var claim = Principal?.FindFirstValue(_options.UserIdClaim);
            return claim is not null && Guid.TryParse(claim, out var id) ? id : null;
        }
    }

    public string? Email => Principal?.FindFirstValue(_options.EmailClaim);

    public Guid? TenantGuid
    {
        get
        {
            var claim = Principal?.FindFirstValue(_options.TenantGuidClaim);
            return claim is not null && Guid.TryParse(claim, out var id) ? id : null;
        }
    }

    public IReadOnlySet<string> Roles
    {
        get
        {
            // Resolved server-side per request (parallel to Permissions) — roles are kept out of
            // the JWT, so prefer the middleware-populated set; fall back to claims when present.
            if (_httpContextAccessor.HttpContext?.Items[PermissionResolutionMiddleware.RolesItemsKey] is IReadOnlySet<string> resolved)
                return resolved;
            return Principal?.FindAll(_options.RoleClaim)
                .SelectMany(c => c.Value.Split(RoleSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .ToHashSet()
                ?? new HashSet<string>();
        }
    }

    // A role claim value may be joined with ',' or ';' depending on producer (TokenServiceAdapter
    // emits ';'); split on both so the reader is robust regardless.
    private static readonly char[] RoleSeparators = [',', ';'];

    public IReadOnlySet<string> Permissions =>
        _httpContextAccessor.HttpContext?.Items[PermissionResolutionMiddleware.ItemsKey]
            as IReadOnlySet<string>
        ?? new HashSet<string>();

    public string? GetClaim(string claimType) => Principal?.FindFirstValue(claimType);
}
