using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Stores;
using Birko.Security.Grants.Models;

namespace Birko.Security.Grants.Stores;

/// <summary>Persistence for <see cref="Invitation"/>s on any Birko backend; lookups are default methods, as on <see cref="IResourceGrantStore"/>.</summary>
public interface IInvitationStore : IAsyncBulkStore<Invitation>
{
    /// <summary>The invitation whose token hashes to <paramref name="tokenHash"/>, whatever its state.</summary>
    Task<Invitation?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default)
        => ReadFirstAsync(i => i.TokenHash == tokenHash, ct);

    /// <summary>Invitations for a normalized email that are neither redeemed, revoked nor expired at <paramref name="now"/>.</summary>
    Task<IEnumerable<Invitation>> ListPendingForEmailAsync(string normalizedEmail, DateTime now, CancellationToken ct = default)
        => ReadAsync(i => i.Email == normalizedEmail && i.RedeemedAt == null && i.RevokedAt == null && i.ExpiresAt > now, null, null, null, ct);
}
