using System;
using Birko.Data.Models;

namespace Birko.Security.Grants.Models;

/// <summary>
/// An offer of a role on a resource to someone identified only by email, redeemed when they first sign in with that
/// email verified. Only <see cref="TokenHash"/> is stored; the token itself is returned once, at creation.
/// </summary>
public class Invitation : AbstractModel
{
    /// <summary>Trimmed and lower-cased (invariant) at creation; compared the same way at sign-in.</summary>
    public string Email { get; set; } = string.Empty;

    public string ResourceType { get; set; } = string.Empty;

    /// <summary>The resource, or <c>null</c> for a type-wide grant.</summary>
    public Guid? ResourceId { get; set; }

    public string Role { get; set; } = string.Empty;

    public Guid? TenantGuid { get; set; }

    /// <summary>SHA-256 of the invitation token, upper-case hex.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>Who invited; the grant is written as granted by them, and only while they may still grant it.</summary>
    public Guid InvitedBy { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RedeemedAt { get; set; }
    public Guid? RedeemedBy { get; set; }
    public DateTime? RevokedAt { get; set; }
}
