using System.Threading;
using System.Threading.Tasks;

namespace Birko.Security.Jwt.OpenIdConnect;

/// <summary>
/// Verifies an <b>inbound</b> OpenID Connect id token against its provider's published keys and our
/// configured client id — the "sign in with Google/Microsoft/Apple" side of OAuth.
/// <para>
/// This is the counterpart the framework was missing. <c>Birko.Communication.OAuth</c> is an OAuth
/// <i>client</i> (it obtains tokens for us to send elsewhere and carries an <c>IdToken</c> it never
/// checks), and <c>Birko.Security.OAuth.Server</c> is an authorization <i>server</i> (it mints our own
/// tokens). Neither answers "did this third party really vouch for this user?".
/// </para>
/// <para>
/// <b>Why it matters:</b> a provider subject identifier (Google <c>sub</c>, GitHub id, Microsoft
/// <c>oid</c>) is a stable <i>public</i> value — it appears in profile APIs, exports and logs. Any login
/// endpoint that resolves an account from a caller-supplied provider key, rather than from a verified
/// token, is a pre-authentication account takeover: know the key, become the user. Take the account key
/// from <see cref="VerifiedOidcIdentity.Subject"/> and from nowhere else.
/// </para>
/// </summary>
public interface IOidcIdTokenVerifier
{
    /// <summary>
    /// Verifies <paramref name="idToken"/> as issued by <paramref name="provider"/>. Checks the signature
    /// against the provider's JWKS, <c>iss</c>, <c>aud</c> (must be our configured client id),
    /// <c>exp</c>/<c>nbf</c>, an asymmetric-only algorithm allow-list, and a non-empty <c>sub</c>.
    /// </summary>
    /// <returns>
    /// Never throws for an untrusted input — an unknown/incomplete provider, a missing token and a bad
    /// token all come back as a refusal with an <see cref="OidcVerificationOutcome"/> the caller maps to
    /// its own error surface.
    /// </returns>
    Task<OidcVerificationResult> VerifyAsync(
        string? provider, string? idToken, CancellationToken ct = default);
}
