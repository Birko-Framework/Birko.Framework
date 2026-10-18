using System.Threading;
using System.Threading.Tasks;
using Birko.Data.InMemory.Stores;
using Birko.Data.Stores;
using Birko.Security.OAuth.Server.Models;
using Birko.Security.OAuth.Server.Stores;

namespace Birko.Security.OAuth.Server.InMemory.Stores;

// In-memory stores for the OAuth authorization server, on Birko.Data.InMemory's AsyncInMemoryStore<T> (thread-safe,
// hands out copies, no persistence: every restart forgets clients, codes and refresh tokens). The named lookups come
// from the store interfaces' default methods. Register each as ONE shared instance (AddOAuthServerInMemoryStores):
// the device-code flow creates a record in one request and reads it in later ones.

/// <summary>In-memory <see cref="IOAuthClientStore"/>.</summary>
public class InMemoryOAuthClientStore : AsyncInMemoryStore<OAuthClient>, IOAuthClientStore { }

/// <summary>
/// In-memory <see cref="IAuthorizationCodeStore"/>. Redemption is atomic within this instance: the claim runs under
/// a lock, so two concurrent redemptions of one code cannot both succeed (CR-L351). Across processes nothing is
/// shared anyway.
/// </summary>
public class InMemoryAuthorizationCodeStore : AsyncInMemoryStore<AuthorizationCode>, IAuthorizationCodeStore
{
    private readonly SemaphoreSlim _claim = new(1, 1);

    /// <inheritdoc />
    public async Task<bool> TryRedeemAsync(AuthorizationCode code, CancellationToken ct = default)
    {
        if (code.Guid is not { } guid) return false;
        await _claim.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var current = await ReadAsync(guid, ct).ConfigureAwait(false);
            if (current == null || current.Used) return false;
            current.Used = true;
            await UpdateAsync(current, ct: ct).ConfigureAwait(false);
        }
        finally
        {
            _claim.Release();
        }
        code.Used = true;
        return true;
    }
}

/// <summary>
/// In-memory <see cref="IRefreshTokenStore"/>. Rotation's claim (<see cref="TryRevokeAsync"/>) is atomic within this
/// instance, and a family is revoked with one filtered update.
/// </summary>
public class InMemoryRefreshTokenStore : AsyncInMemoryStore<RefreshTokenRecord>, IRefreshTokenStore
{
    private readonly SemaphoreSlim _claim = new(1, 1);

    /// <inheritdoc />
    public async Task<bool> TryRevokeAsync(RefreshTokenRecord record, CancellationToken ct = default)
    {
        if (record.Guid is not { } guid) return false;
        await _claim.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var current = await ReadAsync(guid, ct).ConfigureAwait(false);
            if (current == null || current.Revoked) return false;
            current.Revoked = true;
            await UpdateAsync(current, ct: ct).ConfigureAwait(false);
        }
        finally
        {
            _claim.Release();
        }
        record.Revoked = true;
        return true;
    }

    /// <inheritdoc />
    public async Task RevokeFamilyAsync(string clientId, string userId, CancellationToken ct = default)
    {
        await _claim.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await UpdateAsync(r => r.ClientId == clientId && r.UserId == userId && !r.Revoked,
                new PropertyUpdate<RefreshTokenRecord>().Set(r => r.Revoked, true), ct).ConfigureAwait(false);
        }
        finally
        {
            _claim.Release();
        }
    }
}

/// <summary>In-memory <see cref="IDeviceCodeStore"/>.</summary>
public class InMemoryDeviceCodeStore : AsyncInMemoryStore<DeviceCodeRecord>, IDeviceCodeStore { }

/// <summary>In-memory <see cref="IConsentStore"/>.</summary>
public class InMemoryConsentStore : AsyncInMemoryStore<ConsentRecord>, IConsentStore { }
