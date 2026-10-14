using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.Security.AspNetCore.Authorization;
using Birko.Security.Grants.Services;
using Birko.Security.Grants.Stores;

namespace Birko.Security.Grants.AspNetCore.Authorization;

/// <summary>
/// An admin is a holder of the reserved admin grant, or a user whose permissions from the host's own
/// <see cref="IUserPermissionResolver"/> include <c>"*"</c> — so an app's existing superadmins are grant admins too.
/// </summary>
public sealed class UserPermissionResolverAdminSource : IGrantAdminSource
{
    private readonly IUserPermissionResolver _inner;
    private readonly BootstrapAdminGrantSource _grants;

    /// <param name="inner">The host's own resolver — never <see cref="GrantAwareUserPermissionResolver"/>. The decorator already
    /// turns the admin grant into <c>"*"</c>, so consulting it here reads the same grant through a second path, and a
    /// container that hands the decorator to this source has the two registrations crossed.</param>
    public UserPermissionResolverAdminSource(IUserPermissionResolver inner, IResourceGrantStore store)
    {
        if (inner is GrantAwareUserPermissionResolver)
            throw new ArgumentException(
                "Pass the host's own IUserPermissionResolver, not the grant-aware decorator: the admin source must ask what the host says, and the decorator only repeats the admin grant this source already reads.",
                nameof(inner));
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _grants = new BootstrapAdminGrantSource(store ?? throw new ArgumentNullException(nameof(store)));
    }

    public async Task<bool> IsAdminAsync(Guid userGuid, Guid? tenantGuid, CancellationToken ct = default)
    {
        if (await _grants.IsAdminAsync(userGuid, tenantGuid, ct).ConfigureAwait(false))
            return true;
        var permissions = await _inner.GetAsync(userGuid, tenantGuid, ct).ConfigureAwait(false);
        return permissions.Contains("*");
    }
}
