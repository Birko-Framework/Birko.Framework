using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Security.AspNetCore.Authorization;
using Birko.Security.Grants.Models;
using Birko.Security.Grants.Stores;

namespace Birko.Security.Grants.AspNetCore.Authorization;

/// <summary>
/// Decorates the host's <see cref="IUserPermissionResolver"/> so grants reach <c>ICurrentUser</c> through the existing
/// <c>PermissionResolutionMiddleware</c>:
/// <list type="bullet">
/// <item>a holder of the reserved admin grant gets <c>"*"</c>, so <c>RequirePermission</c> lets them through everywhere;</item>
/// <item>every type-wide grant adds <c>"{type}:{action}"</c> for each action of its role, and <c>"{type}:{role}"</c> as a role.</item>
/// </list>
/// Per-resource grants are deliberately <b>not</b> surfaced: there is one per resource, unboundedly many, and
/// <c>RequireGrant</c> checks them where the resource id is known.
/// </summary>
/// <remarks>
/// A stored type-wide grant whose role or resource type is no longer in the table is <b>skipped here</b> rather than
/// thrown. This runs inside a middleware on every authenticated request, so throwing would turn every endpoint into
/// a 500 for that user, anonymous ones included, the moment a deploy retires a role. The grant is not silently
/// honoured either: it contributes nothing, and <c>IGrantService</c> still refuses loudly wherever that type or role
/// is actually checked.
/// </remarks>
public sealed class GrantAwareUserPermissionResolver : IUserPermissionResolver
{
    private readonly IUserPermissionResolver _inner;
    private readonly IResourceGrantStore _store;
    private readonly GrantRoleTable _table;
    private readonly HashSet<string> _types;
    private readonly HashSet<string> _roles;

    public GrantAwareUserPermissionResolver(IUserPermissionResolver inner, IResourceGrantStore store, GrantRoleTable table)
    {
        if (inner is GrantAwareUserPermissionResolver)
            throw new ArgumentException("The grant-aware resolver is already in place; decorating it twice adds nothing.", nameof(inner));
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _table = table ?? throw new ArgumentNullException(nameof(table));
        _types = new HashSet<string>(table.ResourceTypes, StringComparer.Ordinal);
        _roles = new HashSet<string>(table.Roles, StringComparer.Ordinal);
    }

    public async Task<IReadOnlySet<string>> GetAsync(Guid userId, Guid? tenantId, CancellationToken ct = default)
    {
        var permissions = new HashSet<string>(await _inner.GetAsync(userId, tenantId, ct).ConfigureAwait(false), StringComparer.Ordinal);
        if (await _store.HasAdminGrantAsync(userId, tenantId, ct).ConfigureAwait(false))
            permissions.Add("*");
        foreach (var grant in await DeclaredTypeWideAsync(userId, tenantId, ct).ConfigureAwait(false))
        {
            foreach (var action in _table.ActionsOf(grant.Role))
                permissions.Add($"{grant.ResourceType}:{action}");
        }
        return permissions;
    }

    public async Task<IReadOnlySet<string>> GetRolesAsync(Guid userId, Guid? tenantId, CancellationToken ct = default)
    {
        var roles = new HashSet<string>(await _inner.GetRolesAsync(userId, tenantId, ct).ConfigureAwait(false), StringComparer.Ordinal);
        foreach (var grant in await DeclaredTypeWideAsync(userId, tenantId, ct).ConfigureAwait(false))
            roles.Add($"{grant.ResourceType}:{grant.Role}");
        return roles;
    }

    private async Task<IEnumerable<ResourceGrant>> DeclaredTypeWideAsync(Guid userId, Guid? tenantId, CancellationToken ct)
        => (await _store.ListTypeWideForUserAsync(userId, tenantId, ct).ConfigureAwait(false))
            .Where(g => _types.Contains(g.ResourceType) && _roles.Contains(g.Role));
}
