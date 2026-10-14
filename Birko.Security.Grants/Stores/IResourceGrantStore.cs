using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Stores;
using Birko.Security.Grants.Models;

namespace Birko.Security.Grants.Stores;

/// <summary>
/// Persistence for <see cref="ResourceGrant"/>s on any Birko backend. The named lookups are default methods over the
/// bulk read surface, so a bare subclass of a backend's async bulk store suffices; every predicate comes from
/// <see cref="GrantPredicates"/>, which owns tenant scoping.
/// </summary>
/// <remarks>
/// Lookups returning several grants use the five-argument <c>ReadAsync</c>: on a bulk store a one-argument
/// <c>ReadAsync(filter)</c> resolves to the collection overload anyway, and spelling the overload out keeps that
/// visible. A single grant is read with <c>ReadFirstAsync</c>.
/// </remarks>
public interface IResourceGrantStore : IAsyncBulkStore<ResourceGrant>
{
    /// <summary>The user's exact and type-wide grants on <paramref name="resourceType"/>, in tenant scope.</summary>
    Task<IEnumerable<ResourceGrant>> ListForUserAsync(Guid userGuid, string resourceType, Guid? tenantGuid, CancellationToken ct = default)
        => ReadAsync(GrantPredicates.ForUser(userGuid, resourceType, tenantGuid), null, null, null, ct);

    /// <summary>Every grant applying to one resource — on it or type-wide — in tenant scope.</summary>
    Task<IEnumerable<ResourceGrant>> ListForResourceAsync(string resourceType, Guid resourceId, Guid? tenantGuid, CancellationToken ct = default)
        => ReadAsync(GrantPredicates.ForResource(resourceType, resourceId, tenantGuid), null, null, null, ct);

    /// <summary>The one grant with exactly this (user, type, resource, tenant), if any.</summary>
    Task<ResourceGrant?> GetExactAsync(Guid userGuid, string resourceType, Guid? resourceId, Guid? tenantGuid, CancellationToken ct = default)
        => ReadFirstAsync(GrantPredicates.Exact(userGuid, resourceType, resourceId, tenantGuid), ct);

    /// <summary>How many grants of <paramref name="role"/> sit on exactly (type, resource, tenant).</summary>
    Task<long> CountRoleAsync(string resourceType, Guid resourceId, string role, Guid? tenantGuid, CancellationToken ct = default)
        => CountAsync(GrantPredicates.RoleOn(resourceType, resourceId, role, tenantGuid), ct);

    /// <summary>Whether the user holds the global admin grant in tenant scope.</summary>
    async Task<bool> HasAdminGrantAsync(Guid userGuid, Guid? tenantGuid, CancellationToken ct = default)
        => await CountAsync(GrantPredicates.Admin(userGuid, tenantGuid), ct).ConfigureAwait(false) > 0;
}
