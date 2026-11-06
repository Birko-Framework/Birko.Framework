using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Security.Authorization;

namespace Birko.Security.AspNetCore;

/// <summary>
/// Permission checker that reads from <see cref="ICurrentUser"/> JWT claims.
/// No database hit — permissions are embedded in the token.
/// Supports wildcard "*" for superadmin access.
/// </summary>
public sealed class ClaimsPermissionChecker : IPermissionChecker
{
    private readonly ICurrentUser _currentUser;

    public ClaimsPermissionChecker(ICurrentUser currentUser)
    {
        _currentUser = currentUser;
    }

    // SH-L219: only an AUTHENTICATED caller holds permissions, as PermissionEndpointFilter already required (401 before any
    // permission is read). A principal a non-authenticating middleware filled with claims passed here, "*" included.
    private bool IsCaller(Guid userId) => _currentUser.IsAuthenticated && _currentUser.UserGuid == userId;

    public Task<bool> HasPermissionAsync(Guid userId, string permission, CancellationToken ct = default)
    {
        if (!IsCaller(userId))
            return Task.FromResult(false);

        var has = _currentUser.Permissions.Contains(permission)
               || _currentUser.Permissions.Contains(WildcardPermission.Value);

        return Task.FromResult(has);
    }

    public Task<IReadOnlyList<string>> GetPermissionsAsync(Guid userId, CancellationToken ct = default)
    {
        if (!IsCaller(userId))
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        return Task.FromResult<IReadOnlyList<string>>(_currentUser.Permissions.ToList());
    }
}
