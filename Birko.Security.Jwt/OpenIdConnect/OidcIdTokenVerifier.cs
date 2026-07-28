using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
// Birko.Security also defines a TokenValidationResult, and it wins here because this file sits inside
// the Birko.Security.* namespace hierarchy. Alias the IdentityModel one explicitly.
using MsTokenValidationResult = Microsoft.IdentityModel.Tokens.TokenValidationResult;

namespace Birko.Security.Jwt.OpenIdConnect;

/// <summary>
/// Default <see cref="IOidcIdTokenVerifier"/> — validates an inbound OIDC id token against the
/// provider's published JWKS and the configured client id.
/// <para>
/// Configuration-source agnostic on purpose: it takes an already-resolved provider map, so a consumer
/// can bind it from `IConfiguration`, a database, or a literal — the framework does not dictate config
/// keys. What the framework <i>does</i> own is the rule that an unknown or incompletely configured
/// provider is <b>refused</b>, never waved through.
/// </para>
/// </summary>
public class OidcIdTokenVerifier : IOidcIdTokenVerifier
{
    /// <summary>
    /// Asymmetric signature algorithms only.
    /// <para>
    /// This allow-list is load-bearing, not hygiene. A provider's signing key is, by definition, public.
    /// If a symmetric algorithm were accepted, an attacker could re-sign a token they authored with
    /// <c>HS256</c> using that public key as the HMAC secret, and the very same key material would verify
    /// it — the classic algorithm-confusion attack. <c>none</c> is excluded for the same reason.
    /// </para>
    /// </summary>
    private static readonly string[] AllowedAlgorithms =
    {
        SecurityAlgorithms.RsaSha256,
        SecurityAlgorithms.RsaSha384,
        SecurityAlgorithms.RsaSha512,
        SecurityAlgorithms.RsaSsaPssSha256,
        SecurityAlgorithms.RsaSsaPssSha384,
        SecurityAlgorithms.RsaSsaPssSha512,
        SecurityAlgorithms.EcdsaSha256,
        SecurityAlgorithms.EcdsaSha384,
        SecurityAlgorithms.EcdsaSha512,
    };

    /// <summary>Default tolerance for clock drift between us and the provider.</summary>
    public static readonly TimeSpan DefaultClockSkew = TimeSpan.FromMinutes(2);

    private readonly IOidcSigningKeySource _keySource;
    private readonly IReadOnlyDictionary<string, OidcProviderOptions> _providers;
    private readonly TimeSpan _clockSkew;

    /// <param name="keySource">Where the provider's public keys come from.</param>
    /// <param name="providers">
    /// Configured providers by name. Matched case-insensitively regardless of the dictionary's own
    /// comparer. An empty map means OIDC login is off — every request is refused.
    /// </param>
    /// <param name="clockSkew">Lifetime tolerance; defaults to <see cref="DefaultClockSkew"/>.</param>
    public OidcIdTokenVerifier(
        IOidcSigningKeySource keySource,
        IReadOnlyDictionary<string, OidcProviderOptions> providers,
        TimeSpan? clockSkew = null)
    {
        _keySource = keySource ?? throw new ArgumentNullException(nameof(keySource));
        if (providers is null) throw new ArgumentNullException(nameof(providers));

        // Re-key case-insensitively so "Google" and "google" resolve identically even when the caller
        // handed in an ordinal dictionary. (A map holding both spellings is a real misconfiguration and
        // throws here, loudly, at construction rather than silently preferring one at login time.)
        _providers = providers is Dictionary<string, OidcProviderOptions> d
                     && ReferenceEquals(d.Comparer, StringComparer.OrdinalIgnoreCase)
            ? providers
            : providers.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);

        _clockSkew = clockSkew ?? DefaultClockSkew;
    }

    /// <summary>
    /// Canonical form of a provider name for storage alongside an external account key.
    /// Lower-cased so one external identity maps to exactly one stored link.
    /// </summary>
    public static string Canonical(string provider) => provider.Trim().ToLowerInvariant();

    public async Task<OidcVerificationResult> VerifyAsync(
        string? provider, string? idToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            return OidcVerificationResult.Failed(
                OidcVerificationOutcome.ProviderNotConfigured, "no provider name was supplied");
        }

        if (!_providers.TryGetValue(provider, out var options))
        {
            var reason = _providers.Count == 0
                ? "no OIDC providers are configured"
                : $"provider is not configured (configured: {string.Join(", ", _providers.Keys)})";
            return OidcVerificationResult.Failed(OidcVerificationOutcome.ProviderNotConfigured, reason);
        }

        var missing = options.FirstMissingSetting();
        if (missing is not null)
        {
            return OidcVerificationResult.Failed(
                OidcVerificationOutcome.ProviderNotConfigured,
                $"configuration is incomplete — {missing} is not set");
        }

        if (string.IsNullOrWhiteSpace(idToken))
        {
            return OidcVerificationResult.Failed(
                OidcVerificationOutcome.NoTokenSupplied,
                "no id token was supplied; a bare provider key is never accepted as proof");
        }

        var canonical = Canonical(provider);

        var (validation, hadKeys) = await ValidateAsync(canonical, options, idToken, false, ct)
            .ConfigureAwait(false);

        // Unknown `kid` usually means the provider rotated keys since our last fetch — refresh once
        // before deciding the token is bad.
        if (!validation.IsValid && validation.Exception is SecurityTokenSignatureKeyNotFoundException)
        {
            (validation, hadKeys) = await ValidateAsync(canonical, options, idToken, true, ct)
                .ConfigureAwait(false);
        }

        if (!hadKeys)
        {
            return OidcVerificationResult.Failed(
                OidcVerificationOutcome.SigningKeysUnavailable,
                $"no signing keys are available for provider '{canonical}'");
        }

        if (!validation.IsValid)
        {
            return OidcVerificationResult.Failed(
                OidcVerificationOutcome.TokenInvalid,
                validation.Exception is { } ex
                    ? $"{ex.GetType().Name}: {ex.Message}"
                    : "token did not validate");
        }

        if (validation.SecurityToken is not JsonWebToken jwt)
        {
            return OidcVerificationResult.Failed(
                OidcVerificationOutcome.TokenInvalid, "validated token was not a JWT");
        }

        // Read the subject off the VERIFIED payload rather than the mapped ClaimsIdentity, so no inbound
        // claim-type mapping can shadow or rewrite it.
        var subject = jwt.TryGetPayloadValue<string>("sub", out var sub) ? sub : null;
        if (string.IsNullOrWhiteSpace(subject))
        {
            return OidcVerificationResult.Failed(
                OidcVerificationOutcome.SubjectMissing, "token carries no 'sub' claim");
        }

        var email = jwt.TryGetPayloadValue<string>("email", out var mail) && !string.IsNullOrWhiteSpace(mail)
            ? mail.Trim().ToLowerInvariant()
            : null;

        var displayName =
            jwt.TryGetPayloadValue<string>("name", out var name) && !string.IsNullOrWhiteSpace(name)
                ? name.Trim()
                : jwt.TryGetPayloadValue<string>("preferred_username", out var preferred)
                  && !string.IsNullOrWhiteSpace(preferred)
                    ? preferred.Trim()
                    : null;

        return OidcVerificationResult.Verified(new VerifiedOidcIdentity(
            canonical, subject.Trim(), email, ReadEmailVerified(jwt), displayName));
    }

    /// <returns>
    /// The validation result, and whether any signing key was even available — the two are reported
    /// separately so "we could not verify" is never reported as "the token is bad".
    /// </returns>
    private async Task<(MsTokenValidationResult Validation, bool HadKeys)> ValidateAsync(
        string canonicalProvider,
        OidcProviderOptions options,
        string idToken,
        bool forceRefresh,
        CancellationToken ct)
    {
        var keys = await _keySource
            .GetSigningKeysAsync(canonicalProvider, options, forceRefresh, ct)
            .ConfigureAwait(false);

        if (keys.Count == 0)
            return (new MsTokenValidationResult { IsValid = false }, false);

        var audiences = new List<string>(options.AdditionalAudiences.Length + 1) { options.ClientId };
        audiences.AddRange(options.AdditionalAudiences.Where(a => !string.IsNullOrWhiteSpace(a)));

        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = options.Issuer,
            ValidateAudience = true,
            ValidAudiences = audiences,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = keys,
            RequireSignedTokens = true,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ValidAlgorithms = AllowedAlgorithms,
            ClockSkew = _clockSkew,
        };

        var validation = await new JsonWebTokenHandler()
            .ValidateTokenAsync(idToken, parameters)
            .ConfigureAwait(false);

        return (validation, true);
    }

    /// <summary>
    /// Reads <c>email_verified</c>, which providers send as either a JSON boolean or a string.
    /// Defaults to false — we never assert an email is verified on the provider's behalf.
    /// </summary>
    private static bool ReadEmailVerified(JsonWebToken jwt)
    {
        if (jwt.TryGetPayloadValue<bool>("email_verified", out var flag))
            return flag;
        if (jwt.TryGetPayloadValue<string>("email_verified", out var text))
            return bool.TryParse(text, out var parsed) && parsed;
        return false;
    }
}
