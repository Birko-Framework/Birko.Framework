using System.Security.Cryptography;
using Birko.Security.Jwt.OpenIdConnect;
using FluentAssertions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Birko.Security.Jwt.Tests;

/// <summary>
/// Guards for <see cref="OidcIdTokenVerifier"/> — inbound OpenID Connect id-token verification.
///
/// <para>
/// <b>Why this exists.</b> A provider subject identifier (Google <c>sub</c>, GitHub id, Microsoft
/// <c>oid</c>) is a stable <i>public</i> value. A login endpoint that resolves an account from a
/// caller-supplied provider key rather than from a verified token is a pre-authentication account
/// takeover: know the key, become the user. That defect was found and fixed in a consumer (Symbio,
/// TASK-274) and the verification logic was lifted here, because every Birko consumer that adds social
/// login needs exactly this and none should re-derive it.
/// </para>
///
/// <para>
/// The framework previously had an OAuth <i>client</i> (Birko.Communication.OAuth — obtains tokens for us
/// to send elsewhere, carries an <c>IdToken</c> it never checks) and an authorization <i>server</i>
/// (Birko.Security.OAuth.Server — mints our own tokens), but nothing that answered "did this third party
/// really vouch for this user?".
/// </para>
///
/// Only the key source is stubbed — with a locally generated RSA key — so these run with no network.
/// </summary>
public class OidcIdTokenVerifierTests
{
    private const string Provider = "google";
    private const string Issuer = "https://accounts.google.com";
    private const string ClientId = "test-client.apps.googleusercontent.com";
    private const string Subject = "108461234567890123456";

    /// <summary>The provider's key pair. Only the public half is ever given to the verifier.</summary>
    private static readonly RsaSecurityKey ProviderKey = new(RSA.Create(2048)) { KeyId = "provider-key-1" };

    private static OidcProviderOptions Configured() => new()
    {
        ClientId = ClientId,
        Issuer = Issuer,
        JwksUri = "https://stub.invalid/certs",
    };

    private static OidcIdTokenVerifier Verifier(
        IOidcSigningKeySource? keySource = null,
        params (string Name, OidcProviderOptions Options)[] providers)
    {
        var map = providers.Length == 0
            ? new Dictionary<string, OidcProviderOptions> { [Provider] = Configured() }
            : providers.ToDictionary(p => p.Name, p => p.Options);

        return new OidcIdTokenVerifier(keySource ?? new StubKeySource(), map);
    }

    private static string MintIdToken(
        string subject = Subject,
        string? issuer = Issuer,
        string? audience = ClientId,
        string? email = null,
        bool emailVerified = false,
        string? name = null,
        DateTime? expires = null,
        SigningCredentials? credentials = null)
    {
        var claims = new Dictionary<string, object> { ["sub"] = subject };
        if (email is not null)
        {
            claims["email"] = email;
            claims["email_verified"] = emailVerified;
        }
        if (name is not null) claims["name"] = name;

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Claims = claims,
            Issuer = issuer,
            Audience = audience,
            IssuedAt = DateTime.UtcNow.AddMinutes(-1),
            NotBefore = DateTime.UtcNow.AddMinutes(-1),
            Expires = expires ?? DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = credentials
                ?? new SigningCredentials(ProviderKey, SecurityAlgorithms.RsaSha256),
        });
    }

    private sealed class StubKeySource : IOidcSigningKeySource
    {
        public Task<IReadOnlyCollection<SecurityKey>> GetSigningKeysAsync(
            string provider, OidcProviderOptions options, bool forceRefresh, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyCollection<SecurityKey>>(new[] { (SecurityKey)ProviderKey });
    }

    private sealed class EmptyKeySource : IOidcSigningKeySource
    {
        public Task<IReadOnlyCollection<SecurityKey>> GetSigningKeysAsync(
            string provider, OidcProviderOptions options, bool forceRefresh, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyCollection<SecurityKey>>(Array.Empty<SecurityKey>());
    }

    // ── Fail-closed configuration ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NoProvidersConfigured_Refuses()
    {
        var verifier = new OidcIdTokenVerifier(
            new StubKeySource(), new Dictionary<string, OidcProviderOptions>());

        var result = await verifier.VerifyAsync(Provider, MintIdToken());

        result.IsVerified.Should().BeFalse(
            "absence of configuration must be a refusal, never 'verification not required'");
        result.Outcome.Should().Be(OidcVerificationOutcome.ProviderNotConfigured);
        result.Identity.Should().BeNull();
    }

    [Fact]
    public async Task UnknownProviderName_Refuses()
    {
        var result = await Verifier().VerifyAsync("evil-idp", MintIdToken());

        result.Outcome.Should().Be(OidcVerificationOutcome.ProviderNotConfigured);
    }

    [Theory]
    [InlineData("", Issuer, "https://stub.invalid/certs")]   // no ClientId
    [InlineData(ClientId, "", "https://stub.invalid/certs")] // no Issuer
    [InlineData(ClientId, Issuer, "")]                       // no JwksUri
    public async Task HalfConfiguredProvider_CountsAsUnconfigured(string clientId, string issuer, string jwks)
    {
        var verifier = Verifier(null, (Provider, new OidcProviderOptions
        {
            ClientId = clientId, Issuer = issuer, JwksUri = jwks,
        }));

        var result = await verifier.VerifyAsync(Provider, MintIdToken());

        result.Outcome.Should().Be(OidcVerificationOutcome.ProviderNotConfigured,
            "a partially configured provider is not verifiable, so it must be refused like an absent one");
    }

    [Fact]
    public async Task ProviderNameIsCaseInsensitive_AndTheIdentityCarriesTheCanonicalName()
    {
        var result = await Verifier().VerifyAsync("GOOGLE", MintIdToken());

        result.IsVerified.Should().BeTrue();
        result.Identity!.Provider.Should().Be("google",
            "the canonical name is what a consumer stores alongside the subject, so it must be stable");
    }

    [Fact]
    public async Task OrdinalProviderMap_IsStillMatchedCaseInsensitively()
    {
        // A consumer that builds an ordinal dictionary must not silently get case-sensitive lookups.
        var verifier = new OidcIdTokenVerifier(
            new StubKeySource(),
            new Dictionary<string, OidcProviderOptions>(StringComparer.Ordinal) { ["google"] = Configured() });

        (await verifier.VerifyAsync("Google", MintIdToken())).IsVerified.Should().BeTrue();
    }

    // ── No token, no keys ────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task NoIdToken_Refuses(string? idToken)
    {
        var result = await Verifier().VerifyAsync(Provider, idToken);

        result.Outcome.Should().Be(OidcVerificationOutcome.NoTokenSupplied);
    }

    [Fact]
    public async Task ABareSubjectIdentifier_IsNotAToken()
    {
        // The original exploit payload: the victim's public subject, pasted where proof belongs.
        var result = await Verifier().VerifyAsync(Provider, Subject);

        result.IsVerified.Should().BeFalse();
        result.Outcome.Should().Be(OidcVerificationOutcome.TokenInvalid);
    }

    [Fact]
    public async Task NoSigningKeysAvailable_RefusesAndIsReportedAsSuch()
    {
        var result = await Verifier(new EmptyKeySource()).VerifyAsync(Provider, MintIdToken());

        result.IsVerified.Should().BeFalse("a JWKS failure must fail closed, not trust the payload");
        result.Outcome.Should().Be(OidcVerificationOutcome.SigningKeysUnavailable,
            "'we could not verify' must be distinguishable from 'the token is bad'");
    }

    // ── Token validation ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TamperedSignature_Refuses()
    {
        var parts = MintIdToken().Split('.');
        parts[2] = parts[2][..^1] + (parts[2][^1] == 'A' ? 'B' : 'A');

        var result = await Verifier().VerifyAsync(Provider, string.Join('.', parts));

        result.Outcome.Should().Be(OidcVerificationOutcome.TokenInvalid);
    }

    [Fact]
    public async Task TokenSignedByAnUnknownKey_Refuses()
    {
        var attackerKey = new RsaSecurityKey(RSA.Create(2048)) { KeyId = "attacker-key" };

        var result = await Verifier().VerifyAsync(Provider, MintIdToken(
            credentials: new SigningCredentials(attackerKey, SecurityAlgorithms.RsaSha256)));

        result.Outcome.Should().Be(OidcVerificationOutcome.TokenInvalid,
            "anyone can generate a key pair; only the provider's published keys count");
    }

    [Fact]
    public async Task HmacTokenSignedWithTheProvidersPublicKey_Refuses()
    {
        // Algorithm confusion. The provider's signing key is public by definition, so if a symmetric
        // algorithm were accepted the same key material that verifies RS256 would also verify a token
        // the attacker authored and HMAC'd with that public value. This is what ValidAlgorithms prevents.
        var publicModulus = ProviderKey.Rsa!.ExportParameters(false).Modulus!;

        var result = await Verifier().VerifyAsync(Provider, MintIdToken(
            credentials: new SigningCredentials(
                new SymmetricSecurityKey(publicModulus), SecurityAlgorithms.HmacSha256)));

        result.Outcome.Should().Be(OidcVerificationOutcome.TokenInvalid,
            "symmetric algorithms must never be accepted for a provider-signed token");
    }

    [Fact]
    public async Task TokenMintedForAnotherRelyingParty_Refuses()
    {
        // Correctly signed by the real provider, but issued to somebody else's client id. This is the
        // check that stops a token harvested from another site being replayed here.
        var result = await Verifier().VerifyAsync(Provider, MintIdToken(audience: "some-other-app"));

        result.Outcome.Should().Be(OidcVerificationOutcome.TokenInvalid);
    }

    [Fact]
    public async Task AdditionalAudiences_AreAcceptedWhenConfigured()
    {
        var options = Configured();
        options.AdditionalAudiences = new[] { "sibling-native-client" };

        var result = await Verifier(null, (Provider, options))
            .VerifyAsync(Provider, MintIdToken(audience: "sibling-native-client"));

        result.IsVerified.Should().BeTrue("a deliberately widened audience list must be honoured");
    }

    [Fact]
    public async Task WrongIssuer_Refuses()
    {
        var result = await Verifier().VerifyAsync(Provider, MintIdToken(issuer: "https://evil.example.com"));

        result.Outcome.Should().Be(OidcVerificationOutcome.TokenInvalid);
    }

    [Fact]
    public async Task ExpiredToken_Refuses()
    {
        var result = await Verifier().VerifyAsync(
            Provider, MintIdToken(expires: DateTime.UtcNow.AddMinutes(-30)));

        result.Outcome.Should().Be(OidcVerificationOutcome.TokenInvalid,
            "an expired assertion is not current proof");
    }

    [Fact]
    public async Task TokenWithNoSubject_Refuses()
    {
        var result = await Verifier().VerifyAsync(Provider, MintIdToken(subject: string.Empty));

        result.IsVerified.Should().BeFalse();
        result.Outcome.Should().Be(OidcVerificationOutcome.SubjectMissing,
            "without a sub there is no identity to resolve");
    }

    // ── Claim extraction ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task VerifiedIdentity_ComesFromTheTokenClaims()
    {
        var result = await Verifier().VerifyAsync(Provider, MintIdToken(
            email: "Person@Example.COM", emailVerified: true, name: "  A Person  "));

        result.IsVerified.Should().BeTrue();
        result.Identity!.Subject.Should().Be(Subject);
        result.Identity.Email.Should().Be("person@example.com", "emails are normalized lower-case");
        result.Identity.EmailVerified.Should().BeTrue();
        result.Identity.DisplayName.Should().Be("A Person");
    }

    [Fact]
    public async Task EmailVerified_DefaultsToFalseWhenTheProviderDoesNotAssertIt()
    {
        var withoutClaim = await Verifier().VerifyAsync(Provider, MintIdToken());
        withoutClaim.Identity!.EmailVerified.Should().BeFalse(
            "we never assert an address is verified on the provider's behalf");

        var explicitlyFalse = await Verifier().VerifyAsync(
            Provider, MintIdToken(email: "maybe@example.com", emailVerified: false));
        explicitlyFalse.Identity!.EmailVerified.Should().BeFalse();
    }

    [Fact]
    public async Task FailureReason_NeverLeaksTokenMaterial()
    {
        // Reason is documented as log-safe; a consumer writes it straight to a warning log.
        var token = MintIdToken(issuer: "https://evil.example.com");

        var result = await Verifier().VerifyAsync(Provider, token);

        result.Reason.Should().NotBeNullOrEmpty("a refusal must be diagnosable");
        result.Reason.Should().NotContain(token);
        result.Reason.Should().NotContain(token.Split('.')[1], "the payload segment must not be echoed");
    }
}
