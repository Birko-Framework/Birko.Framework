using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.Security.Grants.Internal;
using Birko.Security.Grants.Models;
using Birko.Security.Grants.Stores;
using Birko.Time;

namespace Birko.Security.Grants.Services;

/// <summary>Default <see cref="IAdminGrantService"/>; also writes bootstrap admins for <see cref="GrantSignInService"/>.</summary>
public sealed class AdminGrantService : IAdminGrantService
{
    private const string Wildcard = GrantRoleTable.Wildcard;

    private readonly IResourceGrantStore _store;
    private readonly IDateTimeProvider _clock;
    private readonly IGrantAdminSource _admins;
    private readonly KeyedLock _locks;

    public AdminGrantService(IResourceGrantStore store, IDateTimeProvider clock, IGrantAdminSource? adminSource = null)
        : this(store, clock, adminSource, KeyedLock.Shared)
    {
    }

    internal AdminGrantService(IResourceGrantStore store, IDateTimeProvider clock, IGrantAdminSource? adminSource, KeyedLock locks)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _admins = adminSource ?? new BootstrapAdminGrantSource(store);
        _locks = locks;
    }

    public async Task<bool> RevokeAdminAsync(Guid callerGuid, Guid userGuid, Guid? tenantGuid = null, CancellationToken ct = default)
    {
        if (callerGuid != userGuid && !await _admins.IsAdminAsync(callerGuid, tenantGuid, ct).ConfigureAwait(false))
            throw new GrantRefusedException(GrantErrorCodes.NotAuthorized, "Only an admin may remove another admin.");

        using var _ = await _locks.AcquireAsync(LockKey(tenantGuid), ct).ConfigureAwait(false);

        var existing = await _store.GetExactAsync(userGuid, Wildcard, null, tenantGuid, ct).ConfigureAwait(false);
        if (existing == null || existing.Role != Wildcard)
            return false;

        if (await AdminsInScopeAsync(tenantGuid, ct).ConfigureAwait(false) <= 1)
            throw LastAdmin();

        await _store.DeleteAsync(GrantPredicates.Exact(userGuid, Wildcard, null, tenantGuid), ct).ConfigureAwait(false);

        // Deleted: the compensation must run even if the caller cancels.
        if (await AdminsInScopeAsync(tenantGuid, CancellationToken.None).ConfigureAwait(false) == 0)
        {
            // Another process removed the other admin between our count and our delete: put this one back.
            await _store.CreateAsync(existing.CopyAsNew(), ct: CancellationToken.None).ConfigureAwait(false);
            throw LastAdmin();
        }
        return true;
    }

    /// <summary>Ensures the user holds the untenanted admin grant. Idempotent: a second call writes nothing.</summary>
    /// <returns><c>true</c> when the grant was written now.</returns>
    internal async Task<bool> EnsureBootstrapAdminAsync(Guid userGuid, CancellationToken ct)
    {
        using var _ = await _locks.AcquireAsync(LockKey(null), ct).ConfigureAwait(false);

        var existing = await _store.GetExactAsync(userGuid, Wildcard, null, null, ct).ConfigureAwait(false);
        if (existing?.Role == Wildcard)
            return false;

        await _store.CreateAsync(new ResourceGrant
        {
            UserGuid = userGuid,
            ResourceType = Wildcard,
            ResourceId = null,
            Role = Wildcard,
            TenantGuid = null,
            GrantedBy = null,
            CreatedAt = _clock.UtcNow,
        }, ct: ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Admin grants that administer <paramref name="tenantGuid"/>: its own, plus the untenanted (global) ones, which
    /// <see cref="BootstrapAdminGrantSource"/> treats as admins in every tenant. A tenant admin is therefore removable while
    /// a global admin exists, and a global admin is counted only among global ones.
    /// </summary>
    private async Task<long> AdminsInScopeAsync(Guid? tenantGuid, CancellationToken ct)
    {
        var count = await _store.CountAdminsAsync(tenantGuid, ct).ConfigureAwait(false);
        if (tenantGuid != null)
            count += await _store.CountAdminsAsync(null, ct).ConfigureAwait(false);
        return count;
    }

    private static string LockKey(Guid? tenantGuid) => $"{Wildcard}\n{Wildcard}\n{tenantGuid?.ToString() ?? "-"}";

    private static GrantRefusedException LastAdmin()
        => new(GrantErrorCodes.LastAdmin, "Removing this admin would leave no admin in its tenant scope.");
}
