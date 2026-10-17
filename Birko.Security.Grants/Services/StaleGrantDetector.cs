using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Security.Grants.Models;
using Birko.Security.Grants.Stores;

namespace Birko.Security.Grants.Services;

/// <summary>Why a stored grant no longer means anything to the role table.</summary>
public enum StaleGrantReason
{
    /// <summary>The grant's role is not declared: it grants nothing.</summary>
    RoleNotDeclared,

    /// <summary>The grant's resource type is not declared: nothing can ask about it.</summary>
    ResourceTypeNotDeclared,
}

/// <summary>A stored grant the role table no longer declares, and why.</summary>
public sealed record StaleGrant(ResourceGrant Grant, StaleGrantReason Reason)
{
    public override string ToString() => Reason == StaleGrantReason.RoleNotDeclared
        ? $"user {Grant.UserGuid} holds undeclared role '{Grant.Role}' on {Where()}"
        : $"user {Grant.UserGuid} holds '{Grant.Role}' on undeclared resource type {Where()}";

    private string Where()
        => $"'{Grant.ResourceType}' {Grant.ResourceId?.ToString() ?? "(type-wide)"}{(Grant.TenantGuid == null ? "" : $" in tenant {Grant.TenantGuid}")}";
}

/// <summary>
/// Lists the stored grants whose role or resource type is no longer declared in the <see cref="GrantRoleTable"/>.
/// </summary>
/// <remarks>
/// TASK-641. A check skips such a grant (it grants nothing), so one stale row cannot make a user's checks or listings
/// throw. That would make a role renamed in configuration quietly remove everyone's access, so this is where it
/// becomes loud: a host runs it at start-up or from a health check, and migrates or revokes what it lists. The
/// reserved admin grant (<c>*</c> / <c>*</c>) is not a consumer grant and is never reported. It reads every grant once.
/// </remarks>
public static class StaleGrantDetector
{
    public static async Task<IReadOnlyList<StaleGrant>> FindAsync(IResourceGrantStore store, GrantRoleTable table, CancellationToken ct = default)
    {
        if (store == null) throw new ArgumentNullException(nameof(store));
        if (table == null) throw new ArgumentNullException(nameof(table));

        var grants = await store.ReadAsync(null, null, null, null, ct).ConfigureAwait(false);
        var stale = new List<StaleGrant>();
        foreach (var grant in grants)
        {
            if (grant.ResourceType == GrantRoleTable.Wildcard && grant.Role == GrantRoleTable.Wildcard)
                continue;
            if (!table.IsResourceType(grant.ResourceType))
                stale.Add(new StaleGrant(grant, StaleGrantReason.ResourceTypeNotDeclared));
            else if (!table.IsRole(grant.Role))
                stale.Add(new StaleGrant(grant, StaleGrantReason.RoleNotDeclared));
        }
        return stale;
    }
}
