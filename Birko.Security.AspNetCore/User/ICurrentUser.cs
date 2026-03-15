using System;
using System.Collections.Generic;

namespace Birko.Security.AspNetCore;

/// <summary>
/// Represents the authenticated user for the current request.
/// Extracted from JWT claims or other authentication schemes.
/// </summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
    string? Email { get; }
    Guid? TenantId { get; }
    IReadOnlySet<string> Roles { get; }
    IReadOnlySet<string> Permissions { get; }
    bool IsAuthenticated { get; }

    /// <summary>
    /// Gets a raw claim value by type. Returns null if not present.
    /// </summary>
    string? GetClaim(string claimType);
}
