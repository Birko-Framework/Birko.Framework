using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.Security.AspNetCore;
using Birko.Security.AspNetCore.Authorization;
using Birko.Security.Grants.Services;
using Birko.Security.Grants.Stores;

namespace Birko.Security.Grants.AspNetCore.Authorization;

/// <summary>
/// An admin is a holder of the reserved admin grant, or a user whose permissions from the host's own
/// <see cref="IUserPermissionResolver"/> include <c>"*"</c> — so an app's existing superadmins are grant admins too.
/// With <see cref="BirkoSecurityOptions.WildcardPermissionEnabled"/> off, the host's <c>"*"</c> makes nobody an admin; the
/// admin grant still does (TASK-823).
/// </summary>
public sealed class UserPermissionResolverAdminSource : IGrantAdminSource
{
    private readonly IUserPermissionResolver _inner;
    private readonly BootstrapAdminGrantSource _grants;
    private readonly BirkoSecurityOptions? _security;

    /// <param name="inner">The host's own resolver — never <see cref="GrantAwareUserPermissionResolver"/>. The decorator already
    /// turns the admin grant into <c>"*"</c>, so consulting it here reads the same grant through a second path, and a
    /// container that hands the decorator to this source has the two registrations crossed.</param>
    /// <param name="security">Its <see cref="BirkoSecurityOptions.WildcardPermissionEnabled"/> is applied to what
    /// <paramref name="inner"/> answers (TASK-823).</param>
    public UserPermissionResolverAdminSource(IUserPermissionResolver inner, IResourceGrantStore store, BirkoSecurityOptions? security = null)
    {
        if (inner is GrantAwareUserPermissionResolver)
            throw new ArgumentException(
                "Pass the host's own IUserPermissionResolver, not the grant-aware decorator: the admin source must ask what the host says, and the decorator only repeats the admin grant this source already reads.",
                nameof(inner));
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _grants = new BootstrapAdminGrantSource(store ?? throw new ArgumentNullException(nameof(store)));
        _security = security;
    }

    public async Task<bool> IsAdminAsync(Guid userGuid, Guid? tenantGuid, CancellationToken ct = default)
    {
        if (await _grants.IsAdminAsync(userGuid, tenantGuid, ct).ConfigureAwait(false))
            return true;
        var permissions = await _inner.GetAsync(userGuid, tenantGuid, ct).ConfigureAwait(false);
        return WildcardPermission.Apply(permissions, _security).Contains(WildcardPermission.Value);
    }
}
