using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Birko.Security.Jwt.OpenIdConnect;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Birko.Security.Jwt.Tests;

/// <summary>
/// TASK-176 — SH-L239: the JWKS cache was keyed by provider name only, so a provider reconfigured to another JWKS URI was
/// served the old endpoint's keys for the rest of the cache window; and nothing collapsed concurrent fetches, so every
/// login at expiry issued its own GET.
/// </summary>
public class Task176JwksCacheTests
{
    /// <summary>Answers each URI with its own key (its kid is the URI's host), counts GETs per URI, and can hold a GET.</summary>
    private sealed class Endpoints : HttpMessageHandler
    {
        public readonly ConcurrentDictionary<string, int> Gets = new();
        public TimeSpan Delay { get; set; } = TimeSpan.Zero;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var uri = request.RequestUri!.ToString();
            Gets.AddOrUpdate(uri, 1, (_, n) => n + 1);
            if (Delay > TimeSpan.Zero) await Task.Delay(Delay, ct);
            var p = RSA.Create(2048).ExportParameters(false);
            var json = $"{{\"keys\":[{{\"kty\":\"RSA\",\"use\":\"sig\",\"kid\":\"{request.RequestUri.Host}\",\"alg\":\"RS256\","
                       + $"\"n\":\"{Base64UrlEncoder.Encode(p.Modulus)}\",\"e\":\"{Base64UrlEncoder.Encode(p.Exponent)}\"}}]}}";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
        }
    }

    private static OidcProviderOptions At(string host) => new() { ClientId = "c", Issuer = "https://issuer", JwksUri = $"https://{host}/certs" };

    [Fact]
    public async Task A_provider_moved_to_another_JWKS_endpoint_gets_the_new_endpoints_keys()
    {
        var endpoints = new Endpoints();
        var source = new HttpOidcSigningKeySource(new HttpClient(endpoints));

        (await source.GetSigningKeysAsync("google", At("old.invalid"), false)).Single().KeyId.Should().Be("old.invalid");
        var moved = await source.GetSigningKeysAsync("google", At("new.invalid"), false);

        moved.Single().KeyId.Should().Be("new.invalid", "the cache is keyed by the endpoint too, not by the provider name alone");
        endpoints.Gets.Should().ContainKey("https://new.invalid/certs");
    }

    [Fact]
    public async Task A_burst_of_logins_on_an_empty_cache_makes_one_request()
    {
        var endpoints = new Endpoints { Delay = TimeSpan.FromMilliseconds(200) };
        var source = new HttpOidcSigningKeySource(new HttpClient(endpoints));

        var results = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => Task.Run(() => source.GetSigningKeysAsync("google", At("idp.invalid"), false))));

        results.Should().OnlyContain(keys => keys.Count == 1);
        endpoints.Gets["https://idp.invalid/certs"].Should().Be(1, "concurrent callers wait for the one fetch in flight");
    }

    [Fact]
    public async Task A_cached_key_set_is_reused_within_its_window() // contract pin
    {
        var endpoints = new Endpoints();
        var source = new HttpOidcSigningKeySource(new HttpClient(endpoints));

        await source.GetSigningKeysAsync("google", At("idp.invalid"), false);
        await source.GetSigningKeysAsync("google", At("idp.invalid"), false);

        endpoints.Gets["https://idp.invalid/certs"].Should().Be(1);
    }
}
