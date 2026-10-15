using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Birko.Security.Jwt.OpenIdConnect;
using Birko.Time;
using FluentAssertions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Birko.Security.Jwt.Tests;

/// <summary>
/// TASK-165: SH-M248 (a repeated claim type made a valid token invalid), SH-M257 (issuer and audience went unchecked
/// when unconfigured), SH-M253 (the OIDC provider was looked up by its raw name) and SH-M259 (stale signing keys were
/// served for ever).
/// </summary>
public class Task165JwtAndOidcTests
{
    private const string Secret = "super-secret-signing-key-of-sufficient-length-1234567890";

    private static TokenOptions Options() => new() { Secret = Secret, Issuer = "birko-issuer", Audience = "birko-audience" };

    private static string Mint(string issuer, string audience, params Claim[] claims)
    {
        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    // ── SH-M248 ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_token_with_a_repeated_claim_type_validates_and_keeps_every_value()
    {
        var provider = new JwtTokenProvider(Options());
        var token = Mint("birko-issuer", "birko-audience",
            new Claim("perm", "orders.read"), new Claim("perm", "orders.write"), new Claim("perm", "orders.read"));

        var result = provider.ValidateToken(token);

        result.IsValid.Should().BeTrue(result.Error);
        result.ClaimValues["perm"].Should().Equal("orders.read", "orders.write");
        result.Claims["perm"].Should().Be("orders.read;orders.write", "the separator TokenServiceAdapter splits on");
    }

    [Fact]
    public void A_single_claim_reads_as_before()
    {
        var provider = new JwtTokenProvider(Options());
        var token = provider.GenerateToken(new Dictionary<string, string> { ["perm"] = "a" }).Token;

        var result = provider.ValidateToken(token);

        result.Claims["perm"].Should().Be("a");
        result.ClaimValues["perm"].Should().Equal("a");
    }

    // ── SH-M257 ──────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null, "birko-audience")]
    [InlineData("birko-issuer", null)]
    [InlineData(" ", "birko-audience")]
    public void Validating_without_an_issuer_or_audience_is_a_configuration_error(string? issuer, string? audience)
    {
        var provider = new JwtTokenProvider(new TokenOptions { Secret = Secret, Issuer = issuer, Audience = audience });
        var token = Mint("a-sibling-service", "its-own-audience");

        provider.Invoking(p => p.ValidateToken(token)).Should().Throw<ArgumentException>().WithParameterName("options");
    }

    [Fact]
    public void A_token_minted_for_another_issuer_with_the_same_secret_is_refused()
    {
        var provider = new JwtTokenProvider(Options());

        provider.ValidateToken(Mint("a-sibling-service", "birko-audience")).IsValid.Should().BeFalse();
        provider.ValidateToken(Mint("birko-issuer", "another-audience")).IsValid.Should().BeFalse();
    }

    [Fact]
    public void A_provider_without_an_issuer_can_still_generate()
    {
        var provider = new JwtTokenProvider(new TokenOptions { Secret = Secret });
        provider.GenerateToken(new Dictionary<string, string> { ["sub"] = "u" }).Token.Should().NotBeNullOrEmpty();
    }

    // ── SH-M253 ──────────────────────────────────────────────────────────────────────────────────

    private const string OidcIssuer = "https://accounts.google.com";
    private const string ClientId = "client.apps.googleusercontent.com";
    private static readonly RsaSecurityKey ProviderKey = new(RSA.Create(2048)) { KeyId = "k1" };

    private sealed class StubKeySource : IOidcSigningKeySource
    {
        public Task<IReadOnlyCollection<SecurityKey>> GetSigningKeysAsync(
            string provider, OidcProviderOptions options, bool forceRefresh, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyCollection<SecurityKey>>(new[] { (SecurityKey)ProviderKey });
    }

    private static OidcProviderOptions Google() => new() { ClientId = ClientId, Issuer = OidcIssuer, JwksUri = "https://stub.invalid/certs" };

    private static string MintIdToken() => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        Claims = new Dictionary<string, object> { ["sub"] = "108" },
        Issuer = OidcIssuer,
        Audience = ClientId,
        IssuedAt = DateTime.UtcNow.AddMinutes(-1),
        NotBefore = DateTime.UtcNow.AddMinutes(-1),
        Expires = DateTime.UtcNow.AddMinutes(10),
        SigningCredentials = new SigningCredentials(ProviderKey, SecurityAlgorithms.RsaSha256),
    });

    [Theory]
    [InlineData(" Google ")]
    [InlineData("google\t")]
    [InlineData("GOOGLE")]
    public async Task A_provider_name_is_looked_up_by_its_canonical_form(string name)
    {
        var verifier = new OidcIdTokenVerifier(new StubKeySource(),
            new Dictionary<string, OidcProviderOptions>(StringComparer.Ordinal) { ["Google"] = Google() });

        var result = await verifier.VerifyAsync(name, MintIdToken());

        result.IsVerified.Should().BeTrue(result.Reason);
        result.Identity!.Provider.Should().Be("google");
    }

    [Fact]
    public void Two_spellings_of_one_provider_are_refused_at_construction()
    {
        var act = () => new OidcIdTokenVerifier(new StubKeySource(),
            new Dictionary<string, OidcProviderOptions>(StringComparer.Ordinal) { ["google"] = Google(), [" Google"] = Google() });

        act.Should().Throw<ArgumentException>();
    }

    // ── SH-M259 ──────────────────────────────────────────────────────────────────────────────────

    private sealed class JwksHandler : HttpMessageHandler
    {
        public bool Up { get; set; } = true;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!Up) throw new HttpRequestException("JWKS endpoint unreachable");
            var p = ProviderKey.Rsa.ExportParameters(false);
            var json = $"{{\"keys\":[{{\"kty\":\"RSA\",\"use\":\"sig\",\"kid\":\"k1\",\"alg\":\"RS256\","
                       + $"\"n\":\"{Base64UrlEncoder.Encode(p.Modulus)}\",\"e\":\"{Base64UrlEncoder.Encode(p.Exponent)}\"}}]}}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }

    [Fact]
    public async Task Stale_keys_are_served_while_the_endpoint_is_down_but_only_up_to_the_maximum_stale_age()
    {
        var handler = new JwksHandler();
        var clock = new TestDateTimeProvider(new DateTimeOffset(2026, 10, 15, 8, 0, 0, TimeSpan.Zero));
        var source = new HttpOidcSigningKeySource(new HttpClient(handler), TimeSpan.FromHours(1), TimeSpan.FromHours(24), clock);

        (await source.GetSigningKeysAsync("google", Google(), false)).Should().ContainSingle();
        source.LastServedStale.Should().BeFalse();

        handler.Up = false;
        clock.Advance(TimeSpan.FromHours(2));
        (await source.GetSigningKeysAsync("google", Google(), false)).Should().ContainSingle("a blip does not fail every login");
        source.LastServedStale.Should().BeTrue();

        clock.Advance(TimeSpan.FromHours(23));
        (await source.GetSigningKeysAsync("google", Google(), false)).Should().BeEmpty(
            "a key revoked while the endpoint was down must stop being accepted");
        source.LastServedStale.Should().BeFalse();
        source.LastFetchError.Should().BeOfType<HttpRequestException>();

        handler.Up = true;
        (await source.GetSigningKeysAsync("google", Google(), false)).Should().ContainSingle("a fresh fetch is served again");
    }
}
