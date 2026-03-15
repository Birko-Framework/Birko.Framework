namespace Birko.Security.AspNetCore;

/// <summary>
/// Standard claim names used by Birko JWT tokens.
/// </summary>
public static class JwtClaimNames
{
    /// <summary>User ID (Guid).</summary>
    public const string UserId = "sub";

    /// <summary>Email address.</summary>
    public const string Email = "email";

    /// <summary>Tenant ID (Guid).</summary>
    public const string TenantId = "tenant_id";

    /// <summary>Permission (repeated for each permission).</summary>
    public const string Permission = "permission";

    /// <summary>Role (repeated for each role).</summary>
    public const string Role = "role";
}
