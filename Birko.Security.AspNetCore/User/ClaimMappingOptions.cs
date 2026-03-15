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

    /// <summary>Claim type for TenantId (Guid). Default: "tenant_id".</summary>
    public string TenantIdClaim { get; set; } = JwtClaimNames.TenantId;

    /// <summary>Claim type for roles (multiple claims). Default: ClaimTypes.Role.</summary>
    public string RoleClaim { get; set; } = ClaimTypes.Role;

    /// <summary>Claim type for permissions (multiple claims). Default: "permission".</summary>
    public string PermissionClaim { get; set; } = JwtClaimNames.Permission;
}
