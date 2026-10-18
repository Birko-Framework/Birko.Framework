using System.Security.Claims;

namespace Birko.Security.AspNetCore;

/// <summary>
/// Configures which JWT claim types map to ICurrentUser properties.
/// </summary>
public class ClaimMappingOptions
{
    /// <summary>Claim type for UserId (Guid). Default: ClaimTypes.NameIdentifier.</summary>
    public string UserIdClaim { get; set; } = ClaimTypes.NameIdentifier;

    /// <summary>Claim type for Email. Default: ClaimTypes.Email.</summary>
    public string EmailClaim { get; set; } = ClaimTypes.Email;

    /// <summary>Claim type for TenantGuid (Guid). Default: "tenant_id".</summary>
    public string TenantGuidClaim { get; set; } = JwtClaimNames.TenantGuid;

    /// <summary>Claim type for roles (multiple claims). Default: ClaimTypes.Role.</summary>
    public string RoleClaim { get; set; } = ClaimTypes.Role;

    /// <summary>Claim type for permissions (multiple claims). Default: "permission".</summary>
    public string PermissionClaim { get; set; } = JwtClaimNames.Permission;

    /// <summary>
    /// Whether a permission claim's value is also split on spaces, beside ',' and ';'. Null (the default) means: only
    /// when <see cref="PermissionClaim"/> is <c>"scope"</c>, the OAuth scope claim, which RFC 6749 §3.3 defines as
    /// space-delimited (a scope token cannot contain a space). TASK-520: with <c>PermissionClaim = "scope"</c>, a
    /// standard <c>"a b"</c> scope read as the one permission <c>"a b"</c>.
    /// </summary>
    public bool? SplitPermissionsOnSpace { get; set; }

    internal bool SplitsPermissionsOnSpace =>
        SplitPermissionsOnSpace ?? string.Equals(PermissionClaim, "scope", System.StringComparison.Ordinal);
}
