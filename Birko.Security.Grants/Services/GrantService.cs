using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Stores;
using Birko.Security.Grants.Internal;
using Birko.Security.Grants.Models;
using Birko.Security.Grants.Stores;
using Birko.Time;

namespace Birko.Security.Grants.Services;

/// <summary>Default <see cref="IGrantService"/> over an <see cref="IResourceGrantStore"/>.</summary>
public sealed class GrantService : IGrantService
{
    private readonly IResourceGrantStore _store;
    private readonly GrantRoleTable _table;
    private readonly IDateTimeProvider _clock;
    private readonly IGrantAdminSource _admins;
    private readonly KeyedLock _locks;

    public GrantService(IResourceGrantStore store, GrantRoleTable table, IDateTimeProvider clock, IGrantAdminSource? adminSource = null)
        : this(store, table, clock, adminSource, KeyedLock.Shared)
    {
    }

    internal GrantService(IResourceGrantStore store, GrantRoleTable table, IDateTimeProvider clock, IGrantAdminSource? adminSource, KeyedLock locks)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _table = table ?? throw new ArgumentNullException(nameof(table));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _admins = adminSource ?? new BootstrapAdminGrantSource(store);
        _locks = locks;
    }

    public async Task<bool> CanAsync(Guid userGuid, string resourceType, Guid? resourceId, string action, Guid? tenantGuid = null, CancellationToken ct = default)
    {
        _table.RequireResourceType(resourceType);
        _table.RequireAction(action);
        if (await _admins.IsAdminAsync(userGuid, tenantGuid, ct).ConfigureAwait(false))
            return true;

        var actions = await EffectiveActionsAsync(userGuid, resourceType, resourceId, tenantGuid, ct).ConfigureAwait(false);
        return actions.Contains(action);
    }

    public async Task<GrantedResources> ListResourcesAsync(Guid userGuid, string resourceType, string action, Guid? tenantGuid = null, CancellationToken ct = default)
    {
        _table.RequireResourceType(resourceType);
        _table.RequireAction(action);
        if (await _admins.IsAdminAsync(userGuid, tenantGuid, ct).ConfigureAwait(false))
            return new GrantedResources(true, Array.Empty<Guid>());

        var grants = (await _store.ListForUserAsync(userGuid, resourceType, tenantGuid, ct).ConfigureAwait(false))
            .Where(g => _table.IsRole(g.Role) && _table.Allows(g.Role, action))
            .ToList();
        var allOfType = grants.Any(g => g.ResourceId == null);
        var ids = grants.Where(g => g.ResourceId != null).Select(g => g.ResourceId!.Value).Distinct().ToList();
        return new GrantedResources(allOfType, ids);
    }

    public async Task<IReadOnlyList<GrantEntry>> ListGranteesAsync(string resourceType, Guid resourceId, Guid? tenantGuid = null, CancellationToken ct = default)
    {
        _table.RequireResourceType(resourceType);
        var grants = await _store.ListForResourceAsync(resourceType, resourceId, tenantGuid, ct).ConfigureAwait(false);
        return grants.Select(g => new GrantEntry(g, g.ResourceId == null)).ToList();
    }

    public Task AuthorizeGrantAsync(Guid callerGuid, string resourceType, Guid? resourceId, string role, Guid? tenantGuid = null, CancellationToken ct = default)
    {
        _table.RequireResourceType(resourceType);
        RequireResourceId(resourceId);
        // Resolved only for a non-admin: an admin may still act on an invitation whose role was retired.
        return AuthorizeAsync(callerGuid, resourceType, resourceId, tenantGuid, () => _table.ActionsOf(role), ct);
    }

    public async Task<ResourceGrant> GrantAsync(Guid callerGuid, Guid userGuid, string resourceType, Guid? resourceId, string role, Guid? tenantGuid = null, CancellationToken ct = default)
    {
        _table.RequireResourceType(resourceType);
        var newActions = _table.ActionsOf(role);
        RequireResourceId(resourceId);

        using var _ = await _locks.AcquireAsync(LockKey(resourceType, resourceId, tenantGuid), ct).ConfigureAwait(false);

        var existing = await _store.GetExactAsync(userGuid, resourceType, resourceId, tenantGuid, ct).ConfigureAwait(false);
        await AuthorizeAsync(callerGuid, resourceType, resourceId, tenantGuid,
            () => existing == null ? newActions : newActions.Union(Held(existing.Role)).ToHashSet(), ct).ConfigureAwait(false);

        return await WriteLockedAsync(callerGuid, userGuid, resourceType, resourceId, role, tenantGuid, existing, ct).ConfigureAwait(false);
    }

    public async Task<ResourceGrant> GrantAtLeastAsync(Guid callerGuid, Guid userGuid, string resourceType, Guid? resourceId, string role, Guid? tenantGuid = null, CancellationToken ct = default)
    {
        _table.RequireResourceType(resourceType);
        var newActions = _table.ActionsOf(role);
        RequireResourceId(resourceId);

        // The coverage decision and the write happen under one resource lock, so a concurrent promotion cannot be
        // overwritten by a role decided against what the user held a moment earlier.
        using var _ = await _locks.AcquireAsync(LockKey(resourceType, resourceId, tenantGuid), ct).ConfigureAwait(false);

        var existing = await _store.GetExactAsync(userGuid, resourceType, resourceId, tenantGuid, ct).ConfigureAwait(false);
        if (existing != null)
        {
            var held = Held(existing.Role);
            if (held.IsSupersetOf(newActions))
                return existing;
            if (!newActions.IsSupersetOf(held))
                throw new GrantRefusedException(GrantErrorCodes.RoleConflict,
                    $"The user's role '{existing.Role}' allows actions '{role}' does not; replacing it would take them away.");
        }

        // From here the new role contains everything held, so authorizing it alone is the GrantAsync rule.
        await AuthorizeAsync(callerGuid, resourceType, resourceId, tenantGuid, () => newActions, ct).ConfigureAwait(false);
        return await WriteLockedAsync(callerGuid, userGuid, resourceType, resourceId, role, tenantGuid, existing, ct).ConfigureAwait(false);
    }

    /// <summary>Creates or replaces the grant. Called with the resource lock held and the write already authorized.</summary>
    private async Task<ResourceGrant> WriteLockedAsync(Guid callerGuid, Guid userGuid, string resourceType, Guid? resourceId, string role,
        Guid? tenantGuid, ResourceGrant? existing, CancellationToken ct)
    {
        if (existing == null)
        {
            var grant = new ResourceGrant
            {
                UserGuid = userGuid,
                ResourceType = resourceType,
                ResourceId = resourceId,
                Role = role,
                TenantGuid = tenantGuid,
                GrantedBy = callerGuid,
                CreatedAt = _clock.UtcNow,
            };
            await _store.CreateAsync(grant, ct: ct).ConfigureAwait(false);
            return grant;
        }

        if (existing.Role == role)
            return existing;

        var demotesOwner = existing.Role == _table.OwnerRole && resourceId != null;
        if (demotesOwner)
            await EnsureNotLastOwnerAsync(resourceType, resourceId!.Value, tenantGuid, ct).ConfigureAwait(false);

        // By the key, not by the entity: a duplicate row another instance wrote for the same key must not keep the old role.
        var key = GrantPredicates.Exact(userGuid, resourceType, resourceId, tenantGuid);
        var previousRole = existing.Role;
        var previousGrantedBy = existing.GrantedBy;
        await _store.UpdateAsync(key, new PropertyUpdate<ResourceGrant>().Set(g => g.Role, role).Set(g => g.GrantedBy, (Guid?)callerGuid), ct).ConfigureAwait(false);
        existing.Role = role;
        existing.GrantedBy = callerGuid;

        // Written: from here on the compensation must run even if the caller cancels.
        if (demotesOwner && await OwnerCountAsync(resourceType, resourceId!.Value, tenantGuid, CancellationToken.None).ConfigureAwait(false) == 0)
        {
            // Another process demoted or removed the other owner between our count and our write.
            await _store.UpdateAsync(key, new PropertyUpdate<ResourceGrant>().Set(g => g.Role, previousRole).Set(g => g.GrantedBy, previousGrantedBy), CancellationToken.None).ConfigureAwait(false);
            throw LastOwner(resourceType, resourceId.Value);
        }
        return existing;
    }

    public async Task<bool> RevokeAsync(Guid callerGuid, Guid userGuid, string resourceType, Guid? resourceId, Guid? tenantGuid = null, CancellationToken ct = default)
    {
        _table.RequireResourceType(resourceType);
        RequireResourceId(resourceId);

        using var _ = await _locks.AcquireAsync(LockKey(resourceType, resourceId, tenantGuid), ct).ConfigureAwait(false);

        var existing = await _store.GetExactAsync(userGuid, resourceType, resourceId, tenantGuid, ct).ConfigureAwait(false);
        if (existing == null)
            return false;

        // Anyone may give up their own access; removing someone else's needs the grant rules.
        if (callerGuid != userGuid)
            await AuthorizeAsync(callerGuid, resourceType, resourceId, tenantGuid, () => Held(existing.Role), ct).ConfigureAwait(false);

        var removesOwner = existing.Role == _table.OwnerRole && resourceId != null;
        if (removesOwner)
            await EnsureNotLastOwnerAsync(resourceType, resourceId!.Value, tenantGuid, ct).ConfigureAwait(false);

        // By the key, not by the entity: a duplicate row for the same key would otherwise keep the access while we report success.
        await _store.DeleteAsync(GrantPredicates.Exact(userGuid, resourceType, resourceId, tenantGuid), ct).ConfigureAwait(false);

        // Deleted: from here on the compensation must run even if the caller cancels.
        if (removesOwner && await OwnerCountAsync(resourceType, resourceId!.Value, tenantGuid, CancellationToken.None).ConfigureAwait(false) == 0)
        {
            // Another process removed the other owner between our count and our delete: put this one back,
            // as a new record — the old one's Guid is gone.
            await _store.CreateAsync(existing.CopyAsNew(), ct: CancellationToken.None).ConfigureAwait(false);
            throw LastOwner(resourceType, resourceId.Value);
        }
        return true;
    }

    public async Task<ResourceGrant> GrantOwnerOnCreateAsync(Guid userGuid, string resourceType, Guid resourceId, Guid? tenantGuid = null, CancellationToken ct = default)
    {
        _table.RequireResourceType(resourceType);
        if (resourceId == Guid.Empty)
            throw new ArgumentException("An owner is granted on one resource; Guid.Empty identifies none.", nameof(resourceId));

        using var _ = await _locks.AcquireAsync(LockKey(resourceType, resourceId, tenantGuid), ct).ConfigureAwait(false);

        var existing = await _store.GetExactAsync(userGuid, resourceType, resourceId, tenantGuid, ct).ConfigureAwait(false);
        if (existing?.Role == _table.OwnerRole)
            return existing;

        // Only a resource nobody owns yet can be claimed: otherwise any caller reaching a create or upsert path with
        // an existing id would make themselves its owner.
        if (await OwnerCountAsync(resourceType, resourceId, tenantGuid, ct).ConfigureAwait(false) > 0)
            throw new GrantRefusedException(GrantErrorCodes.NotAuthorized,
                $"{Describe(resourceType, resourceId)} already has an owner; ownership is granted by an owner, not claimed.");

        if (existing != null)
        {
            await _store.UpdateAsync(GrantPredicates.Exact(userGuid, resourceType, resourceId, tenantGuid),
                new PropertyUpdate<ResourceGrant>().Set(g => g.Role, _table.OwnerRole), ct).ConfigureAwait(false);
            existing.Role = _table.OwnerRole;
            return existing;
        }

        var grant = new ResourceGrant
        {
            UserGuid = userGuid,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Role = _table.OwnerRole,
            TenantGuid = tenantGuid,
            GrantedBy = null,
            CreatedAt = _clock.UtcNow,
        };
        await _store.CreateAsync(grant, ct: ct).ConfigureAwait(false);
        return grant;
    }

    private async Task<HashSet<string>> EffectiveActionsAsync(Guid userGuid, string resourceType, Guid? resourceId, Guid? tenantGuid, CancellationToken ct)
    {
        var grants = await _store.ListForUserAsync(userGuid, resourceType, tenantGuid, ct).ConfigureAwait(false);
        var actions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var grant in grants)
        {
            if (grant.ResourceId != null && grant.ResourceId != resourceId)
                continue;
            actions.UnionWith(Held(grant.Role));
        }
        return actions;
    }

    /// <summary>
    /// The actions a STORED grant's role allows: none when the role has been retired from the table since the grant was
    /// written.
    /// </summary>
    /// <remarks>
    /// TASK-641 (user's decision, 2026-10-17). Resolving it threw, so one stale row made every check of that resource,
    /// every check of the type for a type-wide grant, and every <see cref="ListResourcesAsync"/> of the type throw for
    /// that user (measured). A retired role grants nothing, which fails closed, so replacing or removing such a grant
    /// needs nothing beyond the new role and <c>share</c>. The loud half moved to <see cref="StaleGrantDetector"/>, which
    /// lists these rows so a renamed role is still caught. A role a caller passes in is still refused.
    /// </remarks>
    private IReadOnlySet<string> Held(string role)
        => _table.IsRole(role) ? _table.ActionsOf(role) : NoActions;

    private static readonly IReadOnlySet<string> NoActions = new HashSet<string>(StringComparer.Ordinal);

    /// <param name="required">Resolved only for a non-admin, so an admin can still act on an invitation whose role was
    /// retired (a role passed in is refused). A stored grant's retired role resolves to no actions (<see cref="Held"/>).</param>
    private async Task AuthorizeAsync(Guid callerGuid, string resourceType, Guid? resourceId, Guid? tenantGuid, Func<IReadOnlySet<string>> required, CancellationToken ct)
    {
        if (await _admins.IsAdminAsync(callerGuid, tenantGuid, ct).ConfigureAwait(false))
            return;

        var own = await EffectiveActionsAsync(callerGuid, resourceType, resourceId, tenantGuid, ct).ConfigureAwait(false);
        if (!own.Contains(_table.ShareAction))
            throw new GrantRefusedException(GrantErrorCodes.NotAuthorized,
                $"The caller may not share {Describe(resourceType, resourceId)}.");

        var missing = required().Where(a => !own.Contains(a)).ToList();
        if (missing.Count > 0)
            throw new GrantRefusedException(GrantErrorCodes.Escalation,
                $"The caller lacks [{string.Join(", ", missing)}] on {Describe(resourceType, resourceId)}, so may not grant or change a role that allows them.");
    }

    private async Task EnsureNotLastOwnerAsync(string resourceType, Guid resourceId, Guid? tenantGuid, CancellationToken ct)
    {
        if (await OwnerCountAsync(resourceType, resourceId, tenantGuid, ct).ConfigureAwait(false) <= 1)
            throw LastOwner(resourceType, resourceId);
    }

    private Task<long> OwnerCountAsync(string resourceType, Guid resourceId, Guid? tenantGuid, CancellationToken ct)
        => _store.CountRoleAsync(resourceType, resourceId, _table.OwnerRole, tenantGuid, ct);

    private static GrantRefusedException LastOwner(string resourceType, Guid resourceId)
        => new(GrantErrorCodes.LastOwner, $"{Describe(resourceType, resourceId)} would be left without an owner.");

    private static void RequireResourceId(Guid? resourceId)
    {
        if (resourceId == Guid.Empty)
            throw new ArgumentException("Guid.Empty identifies no resource; pass null for a type-wide grant.", nameof(resourceId));
    }

    private static string LockKey(string resourceType, Guid? resourceId, Guid? tenantGuid)
        => $"{resourceType}\n{resourceId?.ToString() ?? "*"}\n{tenantGuid?.ToString() ?? "-"}";

    private static string Describe(string resourceType, Guid? resourceId)
        => resourceId == null ? $"every '{resourceType}'" : $"'{resourceType}' {resourceId}";
}
