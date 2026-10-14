using System;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Security.Grants.Services;

/// <summary>
/// Writes and removes the reserved admin grant <c>{ResourceType = "*", ResourceId = null, Role = "*"}</c>, which
/// <see cref="IGrantService"/> cannot touch because <c>"*"</c> is never a declared resource type.
/// </summary>
public interface IAdminGrantService
{
    /// <summary>
    /// Removes <paramref name="userGuid"/>'s admin grant with exactly <paramref name="tenantGuid"/>. Only an admin may call
    /// it (anyone may remove their own). Removing the last admin grant of that tenant scope is refused with
    /// <see cref="GrantErrorCodes.LastAdmin"/>. Returns <c>false</c> when there was no such grant.
    /// </summary>
    /// <remarks>
    /// A bootstrap admin removed here is re-created at their next sign-in while their identity is still in
    /// <see cref="GrantSignInOptions.BootstrapAdmins"/>: remove them from the configuration too.
    /// </remarks>
    Task<bool> RevokeAdminAsync(Guid callerGuid, Guid userGuid, Guid? tenantGuid = null, CancellationToken ct = default);
}
