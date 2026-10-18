using System;
using Birko.Communication.AspNetCore;

namespace Birko.Security.AspNetCore;

/// <summary>
/// The owner-or-admin rule for an <see cref="ICurrentUser"/> (TASK-541): a thin layer over
/// <see cref="OwnerAccess"/>, which owns the rule, so a handler and <c>OwnedCrudResults</c> decide the same way.
/// </summary>
/// <remarks>
/// This is why Birko.Security.AspNetCore imports Birko.Communication.AspNetCore (decided by the user 2026-10-18): the rule
/// lives below both, in the project with no Birko dependencies. A denied caller is answered 404, not 403, since owned data
/// is private (rule 65).
/// </remarks>
public static class CurrentUserOwnerAccessExtensions
{
    /// <summary>
    /// Whether the user sees every owner's resources: the wildcard <c>*</c> permission, or
    /// <paramref name="viewAllPermission"/> when one is given.
    /// </summary>
    public static bool SeesEverything(this ICurrentUser user, string? viewAllPermission = null)
    {
        if (user == null) throw new ArgumentNullException(nameof(user));
        return user.Permissions.Contains("*")
            || (!string.IsNullOrEmpty(viewAllPermission) && user.Permissions.Contains(viewAllPermission));
    }

    /// <summary>
    /// Whether the user may access a resource owned by <paramref name="owner"/>: it sees everything, or its
    /// <see cref="ICurrentUser.UserGuid"/> is present and equal to the owner. A null owner is never a match.
    /// </summary>
    public static bool CanAccess(this ICurrentUser user, Guid? owner, string? viewAllPermission = null)
        => OwnerAccess.CanAccess(user.UserGuid, owner, user.SeesEverything(viewAllPermission));

    /// <summary>
    /// The same rule for a string owner id: the caller is <see cref="ICurrentUser.UserGuid"/> in its <c>"D"</c> form,
    /// compared ordinally, as DraCode's <c>ApiOwnership</c> did. A null or empty owner is never a match.
    /// </summary>
    public static bool CanAccess(this ICurrentUser user, string? owner, string? viewAllPermission = null)
        => OwnerAccess.CanAccess(user.UserGuid?.ToString("D"), owner, user.SeesEverything(viewAllPermission));
}
