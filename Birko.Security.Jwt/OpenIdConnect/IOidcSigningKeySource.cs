using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.IdentityModel.Tokens;

namespace Birko.Security.Jwt.OpenIdConnect;

/// <summary>
/// Supplies the public keys an id token from a given provider may be verified against.
/// <para>
/// A seam, not ceremony: it lets <see cref="OidcIdTokenVerifier"/> be tested against a locally
/// generated key with no network, and lets a consumer substitute its own caching/transport policy.
/// </para>
/// </summary>
public interface IOidcSigningKeySource
{
    /// <summary>
    /// Returns the provider's current signing keys, or an <b>empty</b> collection when none could be
    /// obtained. Empty must never be interpreted as "no verification needed" — the verifier refuses.
    /// </summary>
    /// <param name="provider">Canonical provider name (usable as a cache key).</param>
    /// <param name="options">The provider's configuration (supplies the JWKS URI).</param>
    /// <param name="forceRefresh">
    /// Bypass any cache. Used once when a token's <c>kid</c> matches none of the cached keys, so a
    /// provider's key rotation does not lock every user out until the cache expires.
    /// </param>
    Task<IReadOnlyCollection<SecurityKey>> GetSigningKeysAsync(
        string provider, OidcProviderOptions options, bool forceRefresh, CancellationToken ct = default);
}
