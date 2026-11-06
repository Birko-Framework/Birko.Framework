using System.Collections.Generic;
using System.Linq;

namespace Birko.Security.AspNetCore;

/// <summary>
/// The wildcard permission <c>"*"</c> (all access) and the one place <see cref="BirkoSecurityOptions.WildcardPermissionEnabled"/>
/// is applied (SH-L217).
/// </summary>
/// <remarks>
/// The option was read by one check (<c>TenantHeaderClaimGuardMiddleware</c>) and ignored by the rest
/// (<see cref="PermissionEndpointFilter"/>, <c>ClaimsPermissionChecker</c>, <c>SeesEverything</c>), so turning it off
/// closed the superadmin bypass in one place only. It is now applied where the permissions are produced: the
/// <see cref="ICurrentUser"/> implementations Birko registers leave <c>"*"</c> out of
/// <see cref="ICurrentUser.Permissions"/> when it is off, so every reader agrees without knowing the option. An
/// <see cref="ICurrentUser"/> an application writes itself decides for itself.
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
}
