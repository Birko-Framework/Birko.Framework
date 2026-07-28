namespace Birko.Security.Jwt.OpenIdConnect;

/// <summary>
/// The identity an OpenID Connect provider actually vouched for, derived <b>only</b> from a
/// cryptographically verified id token — never from anything the caller asserted alongside it.
/// </summary>
/// <param name="Provider">Canonical (lower-cased) provider name the token was verified against.</param>
/// <param name="Subject">The verified <c>sub</c> claim. This is the only safe external account key.</param>
/// <param name="Email">The <c>email</c> claim, lower-cased, or null if the provider asserted none.</param>
/// <param name="EmailVerified">The provider's <c>email_verified</c> claim; false unless it says otherwise.</param>
/// <param name="DisplayName">The <c>name</c> / <c>preferred_username</c> claim, when present.</param>
public sealed record VerifiedOidcIdentity(
    string Provider,
    string Subject,
    string? Email,
    bool EmailVerified,
    string? DisplayName);

/// <summary>Why an id-token verification succeeded or was refused.</summary>
public enum OidcVerificationOutcome
{
    /// <summary>The token verified; an identity is available.</summary>
    Verified = 0,

    /// <summary>
    /// The named provider is unknown or incompletely configured. Deliberately distinct from
    /// <see cref="TokenInvalid"/>: it is a deployment problem, not a caller problem — but it is still a
    /// refusal, never a bypass.
    /// </summary>
    ProviderNotConfigured,

    /// <summary>No id token was supplied. A bare subject identifier is not proof of anything.</summary>
    NoTokenSupplied,

    /// <summary>
    /// No signing keys could be obtained for the provider (JWKS unreachable, or it published none), so
    /// nothing can be verified. Fails closed.
    /// </summary>
    SigningKeysUnavailable,

    /// <summary>Signature, <c>iss</c>, <c>aud</c>, lifetime or algorithm validation failed.</summary>
    TokenInvalid,

    /// <summary>The token validated but carries no <c>sub</c>, so there is no identity to resolve.</summary>
    SubjectMissing,
}

/// <summary>
/// Outcome of <see cref="IOidcIdTokenVerifier.VerifyAsync"/>.
/// <para>
/// Deliberately a framework-neutral type rather than any consumer's Result/Either: a consumer maps
/// <see cref="Outcome"/> onto its own error codes and localized messages. <see cref="Reason"/> is
/// log-safe — it never contains token material.
/// </para>
/// </summary>
public sealed class OidcVerificationResult
{
    private OidcVerificationResult(
        OidcVerificationOutcome outcome, VerifiedOidcIdentity? identity, string reason)
    {
        Outcome = outcome;
        Identity = identity;
        Reason = reason;
    }

    public OidcVerificationOutcome Outcome { get; }

    /// <summary>Non-null exactly when <see cref="IsVerified"/> is true.</summary>
    public VerifiedOidcIdentity? Identity { get; }

    /// <summary>Log-safe explanation. Never contains the token or any key material.</summary>
    public string Reason { get; }

    public bool IsVerified => Outcome == OidcVerificationOutcome.Verified;

    public static OidcVerificationResult Verified(VerifiedOidcIdentity identity)
        => new(OidcVerificationOutcome.Verified, identity, string.Empty);

    public static OidcVerificationResult Failed(OidcVerificationOutcome outcome, string reason)
        => new(outcome, null, reason);
}
