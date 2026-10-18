using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Stores;
using Birko.Security.OAuth.Server.Models;

namespace Birko.Security.OAuth.Server.Stores;

/// <summary>
/// Persistence for refresh-token records. The token presented by the client is hashed
/// before lookup — callers should pass <see cref="Models.RefreshTokenRecord.TokenHash"/>,
/// not the plaintext.
/// </summary>
public interface IRefreshTokenStore : IAsyncStore<RefreshTokenRecord>
{
    Task<RefreshTokenRecord?> GetByHashAsync(string tokenHash, CancellationToken ct = default)
        => ReadAsync(r => r.TokenHash == tokenHash, ct);

    /// <summary>
    /// Revokes the record if, and only if, it is not revoked yet. Returns false when it already was (or no longer
    /// exists). Rotation uses it to claim the presented token: two concurrent refreshes with one token must not both
    /// get a new pair (TASK-518). On true, <paramref name="record"/>.Revoked is set.
    /// </summary>
    /// <remarks>
    /// The default re-reads the stored record by its Guid and writes it back: NOT atomic. Override it with a
    /// conditional update wherever the store can express one.
    /// </remarks>
    async Task<bool> TryRevokeAsync(RefreshTokenRecord record, CancellationToken ct = default)
    {
        if (record.Guid is not { } guid) return false;
        var current = await ReadAsync(guid, ct).ConfigureAwait(false);
        if (current == null || current.Revoked) return false;
        current.Revoked = true;
        await UpdateAsync(current, ct: ct).ConfigureAwait(false);
        record.Revoked = true;
        return true;
    }

    /// <summary>
    /// CR-L350: Revokes every not-yet-revoked refresh token belonging to the given
    /// (<paramref name="clientId"/>, <paramref name="userId"/>) family. Used for RFC 6819 §5.2.2.3 /
    /// OAuth 2.1 refresh-token reuse detection — presenting an already-rotated (revoked) token signals
    /// possible theft, so the whole family is invalidated rather than just the replayed token.
    /// <para>
    /// The default implementation revokes one record at a time over the single-result read/update surface,
    /// bounded by the current family <see cref="IAsyncCountStore{T}.CountAsync"/> so a store that fails to
    /// persist the flag cannot spin forever. Backends with a native conditional/bulk update should override
    /// this with a single set-based operation.
    /// </para>
    /// </summary>
    async Task RevokeFamilyAsync(string clientId, string userId, CancellationToken ct = default)
    {
        var max = await CountAsync(r => r.ClientId == clientId && r.UserId == userId, ct).ConfigureAwait(false);
        for (long i = 0; i < max; i++)
        {
            var record = await ReadAsync(r => r.ClientId == clientId && r.UserId == userId && !r.Revoked, ct).ConfigureAwait(false);
            if (record == null) break;
            record.Revoked = true;
            await UpdateAsync(record, ct: ct).ConfigureAwait(false);
        }
    }
}
