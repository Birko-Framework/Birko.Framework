using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Birko.Communication.OAuth.OpenIdConnect.Tests;

internal sealed class ManualClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>
/// An OpenID Connect provider behind an <see cref="HttpMessageHandler"/>: a discovery document, a key set and a token
/// endpoint at <c>https://idp.test</c>, signing with RSA keys of its own. The token endpoint answers code <c>good</c>
/// with <see cref="NextIdToken"/> and any other code with <c>invalid_grant</c>.
/// </summary>
internal sealed class FakeOpenIdProvider : HttpMessageHandler
{
    public const string Base = "https://idp.test";
    public const string MetadataAddress = Base + "/.well-known/openid-configuration";

    public string DiscoveryIssuer { get; set; } = Base;
    public List<RsaSecurityKey> PublishedKeys { get; } = new();
    public RsaSecurityKey SigningKey { get; set; }
    public HttpStatusCode DiscoveryStatus { get; set; } = HttpStatusCode.OK;
    public HttpStatusCode? TokenStatusOverride { get; set; }
    public string? NextIdToken { get; set; }
    public int DiscoveryRequests { get; private set; }
    public int JwksRequests { get; private set; }
    public List<string> TokenRequests { get; } = new();

    public FakeOpenIdProvider()
    {
        SigningKey = NewKey("key-1");
        PublishedKeys.Add(SigningKey);
    }

    public static RsaSecurityKey NewKey(string kid)
    {
        using var rsa = RSA.Create(2048);
        return new RsaSecurityKey(rsa.ExportParameters(true)) { KeyId = kid };
    }

    /// <summary>Signs <paramref name="claims"/> as a JWT with <see cref="SigningKey"/>.</summary>
    public string Sign(Dictionary<string, object> claims) => Sign(claims, null);

    /// <summary>Signs <paramref name="claims"/> as a JWT with <paramref name="key"/> (default <see cref="SigningKey"/>).</summary>
    public string Sign(IDictionary<string, object> claims, RsaSecurityKey? key) =>
        new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(
            JsonSerializer.Serialize(claims), new SigningCredentials(key ?? SigningKey, SecurityAlgorithms.RsaSha256));

    /// <summary>An unsigned token (<c>alg: none</c>) with these claims.</summary>
    public static string Unsigned(Dictionary<string, object> claims) =>
        $"{B64("""{"alg":"none","typ":"JWT"}""")}.{B64(JsonSerializer.Serialize(claims))}.";

    private static string B64(string s) => Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(s));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path == "/.well-known/openid-configuration")
        {
            DiscoveryRequests++;
            if (DiscoveryStatus != HttpStatusCode.OK) return Json(DiscoveryStatus, """{"error":"down"}""");
            return Json(HttpStatusCode.OK, JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["issuer"] = DiscoveryIssuer,
                ["authorization_endpoint"] = Base + "/authorize",
                ["token_endpoint"] = Base + "/token",
                ["jwks_uri"] = Base + "/jwks",
            }));
        }
        if (path == "/jwks")
        {
            JwksRequests++;
            var keys = PublishedKeys.Select(k => new Dictionary<string, string>
            {
                ["kty"] = "RSA",
                ["use"] = "sig",
                ["alg"] = "RS256",
                ["kid"] = k.KeyId,
                ["n"] = Base64UrlEncoder.Encode(k.Parameters.Modulus),
                ["e"] = Base64UrlEncoder.Encode(k.Parameters.Exponent),
            });
            return Json(HttpStatusCode.OK, JsonSerializer.Serialize(new { keys }));
        }
        if (path == "/token")
        {
            var body = await request.Content!.ReadAsStringAsync(ct);
            TokenRequests.Add(body);
            if (TokenStatusOverride is { } status) return new HttpResponseMessage(status) { Content = new StringContent("upstream failure") };
            if (!body.Contains("code=good", StringComparison.Ordinal))
                return Json(HttpStatusCode.BadRequest, """{"error":"invalid_grant","error_description":"Bad code."}""");
            var response = new Dictionary<string, object> { ["access_token"] = "at", ["token_type"] = "Bearer", ["expires_in"] = 3600 };
            if (NextIdToken != null) response["id_token"] = NextIdToken;
            return Json(HttpStatusCode.OK, JsonSerializer.Serialize(response));
        }
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
