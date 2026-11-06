using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Birko.Security.AspNetCore.Authorization;

namespace Birko.Security.AspNetCore;

/// <summary>
/// The wildcard permission <c>"*"</c> (all access) and the one place <see cref="BirkoSecurityOptions.WildcardPermissionEnabled"/>
/// is applied (SH-L217).
/// </summary>
/// <remarks>
/// The option was read by one check (<c>TenantHeaderClaimGuardMiddleware</c>) and ignored by the rest
/// (<see cref="PermissionEndpointFilter"/>, <c>ClaimsPermissionChecker</c>, <c>SeesEverything</c>), so turning it off
/// closed the superadmin bypass in one place only. It is applied where the HOST's permission data enters: the claims
/// <see cref="ClaimsCurrentUser"/> reads, and what the host's <see cref="IUserPermissionResolver"/> answers
/// (<see cref="PermissionResolutionMiddleware"/>), so every reader agrees without knowing the option.
/// <para>
/// TASK-823 (the user's decision): the option means "a <c>"*"</c> from the host's own data is not superadmin". A
/// <c>"*"</c> Birko grants itself is kept: the loopback bypass principal's, and the one the grants layer gives a holder
/// of the admin grant (a resolver implementing <see cref="IAppliesWildcardOption"/>).
/// </para>
/// </remarks>
public static class WildcardPermission
{
    /// <summary>The permission that grants every other one.</summary>
    public const string Value = "*";

    /// <summary><paramref name="permissions"/> as the options allow: without <see cref="Value"/> when the wildcard is off.</summary>
    public static IReadOnlySet<string> Apply(IReadOnlySet<string> permissions, BirkoSecurityOptions? options) =>
        options is { WildcardPermissionEnabled: false } && permissions.Contains(Value)
            ? permissions.Where(p => p != Value).ToHashSet()
            : permissions;

    /// <summary>
    /// Claim-borne <paramref name="permissions"/> of <paramref name="principal"/> as the options allow. The loopback bypass
    /// principal is Birko's own, so its <see cref="Value"/> is kept.
    /// </summary>
    public static IReadOnlySet<string> Apply(IReadOnlySet<string> permissions, ClaimsPrincipal? principal, BirkoSecurityOptions? options) =>
        principal?.Identity?.AuthenticationType == LoopbackBypass.AuthenticationType ? permissions : Apply(permissions, options);
}
