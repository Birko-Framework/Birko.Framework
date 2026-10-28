using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Birko.Communication.OAuth.OpenIdConnect;

/// <summary>Who signed in, as a verified <c>id_token</c> says.</summary>
/// <param name="Provider">The provider's name (<see cref="OpenIdProviderOptions.Name"/>).</param>
/// <param name="Subject">The provider's stable id of the user (<c>sub</c>). Unique only within <paramref name="Provider"/>;
/// Microsoft's is per application.</param>
/// <param name="Issuer">The token's issuer (<c>iss</c>).</param>
/// <param name="Email">The <c>email</c> claim, or null. Not proof of ownership unless <paramref name="EmailVerified"/>.</param>
/// <param name="EmailVerified"><c>email_verified</c> (or Microsoft's optional <c>xms_edov</c>) is true. False when the
/// provider does not say: Microsoft's <c>email</c> is not verified by default.</param>
/// <param name="Name">The <c>name</c> claim, or null.</param>
/// <param name="TenantId">The <c>tid</c> claim (Microsoft), or null.</param>
public sealed record OpenIdIdentity(
    string Provider, string Subject, string Issuer, string? Email, bool EmailVerified, string? Name, string? TenantId);

/// <summary>The outcome of <see cref="IdTokenValidator.ValidateAsync"/>.</summary>
/// <param name="Identity">The verified identity, or null.</param>
/// <param name="Error">Why the token was refused, for logs (never show it to the user), or null.</param>
/// <param name="ProviderUnavailable">The provider's keys could not be read, so the token could not be checked.</param>
public sealed record IdTokenValidationResult(OpenIdIdentity? Identity, string? Error, bool ProviderUnavailable)
{
    /// <summary>The token is verified and <see cref="Identity"/> is set.</summary>
    public bool IsValid => Identity != null;
}

/// <summary>
/// Verifies an OpenID Connect <c>id_token</c> against its provider's published keys (TASK-778): an RS256 signature by a
/// key of the provider's key set (fetched again once when the token names a key the cached set lacks), the issuer, the
/// audience (and <c>azp</c> when there are several), <c>exp</c> and <c>nbf</c> on the injected clock, the <c>nonce</c>,
/// and a non-empty <c>sub</c>. Register one per provider as a singleton: it caches the provider's keys.
/// </summary>
public sealed class IdTokenValidator
{
    private const string Rs256 = SecurityAlgorithms.RsaSha256;
    private const string TenantPlaceholder = "{tenantid}";

    private readonly OpenIdProviderOptions _options;
    private readonly TimeProvider _clock;
    private readonly JsonWebTokenHandler _handler = new();

    /// <param name="options">The provider; validated here.</param>
    /// <param name="http">The HttpClient the discovery document and keys are fetched with; not disposed by this class.</param>
    /// <param name="clock">The clock <c>exp</c>, <c>nbf</c> and the key cache follow. Default <see cref="TimeProvider.System"/>.</param>
    public IdTokenValidator(OpenIdProviderOptions options, HttpClient http, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(http);
        options.Validate();
        _options = options;
        _clock = clock ?? TimeProvider.System;
        Metadata = new OpenIdMetadataCache(options, http, _clock);
    }

    /// <summary>The provider this validator checks tokens of.</summary>
    public OpenIdProviderOptions Options => _options;

    internal OpenIdMetadataCache Metadata { get; }

    /// <summary>Verifies <paramref name="idToken"/>, which must carry <paramref name="expectedNonce"/>.</summary>
    /// <param name="idToken">The <c>id_token</c> from the provider's token response.</param>
    /// <param name="expectedNonce">The nonce this sign-in sent in its authorization request.</param>
    public async Task<IdTokenValidationResult> ValidateAsync(string idToken, string expectedNonce, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(idToken)) return Refused("no id_token");
        if (string.IsNullOrEmpty(expectedNonce)) throw new ArgumentException("The expected nonce is required.", nameof(expectedNonce));

        OpenIdMetadata metadata;
        try
        {
            metadata = await Metadata.GetAsync(ct).ConfigureAwait(false);
        }
        catch (OpenIdProviderUnavailableException ex)
        {
            return new IdTokenValidationResult(null, ex.Message, ProviderUnavailable: true);
        }

        var result = await CheckSignatureAsync(idToken, metadata).ConfigureAwait(false);
        // A key the cached set lacks: the provider may have rotated its keys. One refresh, rate-limited by the cache.
        if (!result.IsValid && result.Exception is SecurityTokenInvalidSignatureException)
        {
            var refreshed = await Metadata.RefreshAfterKeyMissAsync(ct).ConfigureAwait(false);
            if (refreshed != null)
            {
                metadata = refreshed;
                result = await CheckSignatureAsync(idToken, metadata).ConfigureAwait(false);
            }
        }
        if (!result.IsValid || result.SecurityToken is not JsonWebToken token)
            return Refused(result.Exception?.GetType().Name ?? "not a signed JWT");

        return Check(token, metadata.Issuer, expectedNonce);
    }

    private Task<TokenValidationResult> CheckSignatureAsync(string idToken, OpenIdMetadata metadata) =>
        _handler.ValidateTokenAsync(idToken, new TokenValidationParameters
        {
            IssuerSigningKeys = metadata.SigningKeys,
            ValidAlgorithms = new[] { Rs256 },
            RequireSignedTokens = true,
            ValidateAudience = true,
            ValidAudience = _options.ClientId,
            // Checked in Check: the issuer has provider-specific rules, and the lifetime follows the injected clock.
            ValidateIssuer = false,
            ValidateLifetime = false,
            RequireExpirationTime = false,
        });

    private IdTokenValidationResult Check(JsonWebToken token, string metadataIssuer, string expectedNonce)
    {
        var tenantId = StringClaim(token, "tid");
        var issuerError = CheckIssuer(token.Issuer, metadataIssuer, tenantId);
        if (issuerError != null) return Refused(issuerError);

        if (_options.AllowedTenantIds.Count > 0
            && !(Guid.TryParse(tenantId, out var tid) && _options.AllowedTenantIds.Contains(tid)))
            return Refused("tenant not allowed");

        var audiences = token.Audiences.ToList();
        var azp = StringClaim(token, "azp");
        if (audiences.Count > 1 && azp == null) return Refused("several audiences and no azp");
        if (azp != null && !string.Equals(azp, _options.ClientId, StringComparison.Ordinal)) return Refused("azp is another client");

        var now = _clock.GetUtcNow();
        if (!token.TryGetPayloadValue<long>("exp", out var exp)) return Refused("no exp");
        if (now > DateTimeOffset.FromUnixTimeSeconds(exp) + _options.ClockSkew) return Refused("expired");
        if (token.TryGetPayloadValue<long>("nbf", out var nbf) && DateTimeOffset.FromUnixTimeSeconds(nbf) - _options.ClockSkew > now)
            return Refused("not yet valid");

        var nonce = StringClaim(token, "nonce");
        if (nonce == null
            || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(nonce), Encoding.UTF8.GetBytes(expectedNonce)))
            return Refused("nonce mismatch");

        if (string.IsNullOrEmpty(token.Subject)) return Refused("no sub");

        var verified = IsTrue(token, "email_verified") || IsTrue(token, "xms_edov");
        return new IdTokenValidationResult(
            new OpenIdIdentity(_options.Name, token.Subject, token.Issuer, StringClaim(token, "email"), verified, StringClaim(token, "name"), tenantId),
            null,
            ProviderUnavailable: false);
    }

    /// <summary>Null when <paramref name="issuer"/> is accepted, else the reason.</summary>
    private string? CheckIssuer(string issuer, string metadataIssuer, string? tenantId)
    {
        if (string.IsNullOrEmpty(issuer)) return "no iss";
        if (_options.AdditionalIssuers.Contains(issuer, StringComparer.Ordinal)) return null;

        var expected = metadataIssuer;
        if (metadataIssuer.Contains(TenantPlaceholder, StringComparison.Ordinal))
        {
            // Microsoft's common / organizations issuer: the token's own tenant goes in the placeholder, and it must be a
            // tenant id, so no other text can be spliced into the issuer to be compared.
            if (tenantId == null || !Guid.TryParseExact(tenantId, "D", out _)) return "tid is not a tenant id";
            expected = metadataIssuer.Replace(TenantPlaceholder, tenantId, StringComparison.Ordinal);
        }
        return string.Equals(issuer, expected, StringComparison.Ordinal) ? null : "wrong issuer";
    }

    private static string? StringClaim(JsonWebToken token, string name) =>
        token.TryGetClaim(name, out var claim) && !string.IsNullOrEmpty(claim.Value) ? claim.Value : null;

    // email_verified is a boolean at Google and Microsoft, a "true" string at some providers; xms_edov may be 1.
    private static bool IsTrue(JsonWebToken token, string name) =>
        token.TryGetClaim(name, out Claim claim)
        && (string.Equals(claim.Value, "true", StringComparison.OrdinalIgnoreCase) || claim.Value == "1");

    private static IdTokenValidationResult Refused(string error) => new(null, error, ProviderUnavailable: false);
}
