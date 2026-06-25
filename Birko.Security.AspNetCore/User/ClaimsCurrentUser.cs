using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Birko.Security.AspNetCore;

/// <summary>
/// Resolves the current user from HttpContext JWT claims.
/// Claim names are configurable via <see cref="ClaimMappingOptions"/>.
/// </summary>
public sealed class ClaimsCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ClaimMappingOptions _options;

    public ClaimsCurrentUser(IHttpContextAccessor httpContextAccessor, ClaimMappingOptions options)
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

    public IReadOnlySet<string> Roles =>
        Principal?.FindAll(_options.RoleClaim)
            .SelectMany(c => c.Value.Split(ClaimSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToHashSet()
        ?? new HashSet<string>();

    // Roles/permissions may be stored either as multiple claims with the same name OR, for JWT
    // compactness, a single claim with a joined value. The producer (TokenServiceAdapter) joins
    // with ';'; older paths used ','. Split on both so callers see discrete values regardless
    // (e.g. `Contains("*")` works for a superadmin + TenantAdmin combo joined as "*,users:user:read,...").
    public IReadOnlySet<string> Permissions =>
        Principal?.FindAll(_options.PermissionClaim)
            .SelectMany(c => c.Value.Split(ClaimSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToHashSet()
        ?? new HashSet<string>();

    private static readonly char[] ClaimSeparators = [',', ';'];

    public string? GetClaim(string claimType) => Principal?.FindFirstValue(claimType);
}
