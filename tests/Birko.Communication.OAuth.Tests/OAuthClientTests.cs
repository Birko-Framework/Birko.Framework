using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace Birko.Communication.OAuth.Tests;

public class OAuthClientTests
{
    private static OAuthSettings CreateSettings(OAuthGrantType grantType = OAuthGrantType.ClientCredentials)
    {
        return new OAuthSettings
        {
            TokenEndpoint = "https://auth.example.com/token",
            ClientId = "test-client",
            ClientSecret = "test-secret",
            Scope = "api.read",
            GrantType = grantType
        };
    }

    [Fact]
    public void Constructor_ThrowsOnNullSettings()
    {
        var act = () => new OAuthClient(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_ThrowsOnEmptyTokenEndpoint()
    {
        var settings = new OAuthSettings { ClientId = "client" };
        var act = () => new OAuthClient(settings);
        act.Should().Throw<ArgumentException>().WithMessage("*TokenEndpoint*");
    }

    [Fact]
    public void Constructor_ThrowsOnEmptyClientId()
    {
        var settings = new OAuthSettings { TokenEndpoint = "https://auth.example.com/token" };
        var act = () => new OAuthClient(settings);
        act.Should().Throw<ArgumentException>().WithMessage("*ClientId*");
    }

    [Fact]
    public async Task GetTokenAsync_ClientCredentials_ReturnsToken()
    {
        var tokenResponse = JsonSerializer.Serialize(new
        {
            access_token = "test-access-token",
            token_type = "Bearer",
            expires_in = 3600,
            scope = "api.read"
        });

        var handler = new FakeHttpHandler(tokenResponse, HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var settings = CreateSettings();

        using var client = new OAuthClient(settings, httpClient);
        var token = await client.GetTokenAsync();

        token.AccessToken.Should().Be("test-access-token");
        token.TokenType.Should().Be("Bearer");
        token.Scope.Should().Be("api.read");
        token.IsExpired.Should().BeFalse();
    }

    [Fact]
    public async Task GetTokenAsync_CachesToken()
    {
        var tokenResponse = JsonSerializer.Serialize(new
        {
            access_token = "cached-token",
            expires_in = 3600
        });

        var handler = new FakeHttpHandler(tokenResponse, HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var settings = CreateSettings();

        using var client = new OAuthClient(settings, httpClient);
        var token1 = await client.GetTokenAsync();
        var token2 = await client.GetTokenAsync();

        token1.AccessToken.Should().Be("cached-token");
        token2.AccessToken.Should().Be("cached-token");
        handler.RequestCount.Should().Be(1); // Only one HTTP request made
    }

    [Fact]
    public async Task GetTokenAsync_ThrowsOnError()
    {
        var errorResponse = JsonSerializer.Serialize(new
        {
            error = "invalid_client",
            error_description = "Client authentication failed"
        });

        var handler = new FakeHttpHandler(errorResponse, HttpStatusCode.Unauthorized);
        var httpClient = new HttpClient(handler);
        var settings = CreateSettings();

        using var client = new OAuthClient(settings, httpClient);
        var act = () => client.GetTokenAsync();
        var ex = await act.Should().ThrowAsync<OAuthException>();
        ex.Which.ErrorCode.Should().Be("invalid_client");
        ex.Which.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task GetTokenAsync_AuthorizationCodeGrant_ThrowsWithoutCode()
    {
        var settings = CreateSettings(OAuthGrantType.AuthorizationCode);
        var handler = new FakeHttpHandler("{}", HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);

        using var client = new OAuthClient(settings, httpClient);
        var act = () => client.GetTokenAsync();
        await act.Should().ThrowAsync<OAuthException>()
            .WithMessage("*Cannot automatically obtain*");
    }

    [Fact]
    public async Task ExchangeCodeAsync_ThrowsOnNullCode()
    {
        var settings = CreateSettings(OAuthGrantType.AuthorizationCode);
        var handler = new FakeHttpHandler("{}", HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);

        using var client = new OAuthClient(settings, httpClient);
        var act = () => client.ExchangeCodeAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ExchangeCodeAsync_SendsCodeAndReturnsToken()
    {
        var tokenResponse = JsonSerializer.Serialize(new
        {
            access_token = "code-token",
            expires_in = 3600,
            refresh_token = "refresh-123"
        });

        var handler = new FakeHttpHandler(tokenResponse, HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var settings = CreateSettings(OAuthGrantType.AuthorizationCode);
        settings.RedirectUri = "http://localhost/callback";

        using var client = new OAuthClient(settings, httpClient);
        var token = await client.ExchangeCodeAsync("auth-code-abc");

        token.AccessToken.Should().Be("code-token");
        token.RefreshToken.Should().Be("refresh-123");
        handler.LastRequestBody.Should().Contain("grant_type=authorization_code");
        handler.LastRequestBody.Should().Contain("code=auth-code-abc");
    }

    [Fact]
    public async Task ExchangeCodeAsync_WithPkce_SendsCodeVerifier()
    {
        var tokenResponse = JsonSerializer.Serialize(new
        {
            access_token = "pkce-token",
            expires_in = 3600
        });

        var handler = new FakeHttpHandler(tokenResponse, HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var settings = CreateSettings(OAuthGrantType.AuthorizationCodePkce);

        using var client = new OAuthClient(settings, httpClient);
        var token = await client.ExchangeCodeAsync("auth-code", codeVerifier: "my-verifier");

        token.AccessToken.Should().Be("pkce-token");
        handler.LastRequestBody.Should().Contain("code_verifier=my-verifier");
        handler.LastRequestBody.Should().NotContain("client_secret"); // PKCE doesn't send client_secret
    }

    [Fact]
    public void BuildAuthorizationUrl_CreatesCorrectUrl()
    {
        var settings = CreateSettings(OAuthGrantType.AuthorizationCode);
        settings.AuthorizationEndpoint = "https://auth.example.com/authorize";
        settings.RedirectUri = "http://localhost/callback";
        settings.Scope = "openid profile";

        var handler = new FakeHttpHandler("{}", HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);

        using var client = new OAuthClient(settings, httpClient);
        var url = client.BuildAuthorizationUrl("my-state");

        url.Should().StartWith("https://auth.example.com/authorize?");
        url.Should().Contain("response_type=code");
        url.Should().Contain("client_id=test-client");
        url.Should().Contain("state=my-state");
        url.Should().Contain("redirect_uri=");
        url.Should().Contain("scope=openid%20profile");
    }

    [Fact]
    public void BuildAuthorizationUrl_WithPkce_IncludesChallenge()
    {
        var settings = CreateSettings(OAuthGrantType.AuthorizationCodePkce);
        settings.AuthorizationEndpoint = "https://auth.example.com/authorize";

        var handler = new FakeHttpHandler("{}", HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);

        using var client = new OAuthClient(settings, httpClient);
        var pkce = PkceChallenge.Generate();
        var url = client.BuildAuthorizationUrl("state", pkce);

        url.Should().Contain("code_challenge=");
        url.Should().Contain("code_challenge_method=S256");
    }

    [Fact]
    public void BuildAuthorizationUrl_ThrowsWithoutEndpoint()
    {
        var settings = CreateSettings(OAuthGrantType.AuthorizationCode);
        var handler = new FakeHttpHandler("{}", HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);

        using var client = new OAuthClient(settings, httpClient);
        var act = () => client.BuildAuthorizationUrl("state");
        act.Should().Throw<OAuthException>().WithMessage("*AuthorizationEndpoint*");
    }

    [Fact]
    public void BuildAuthorizationUrl_ThrowsOnNullState()
    {
        var settings = CreateSettings(OAuthGrantType.AuthorizationCode);
        settings.AuthorizationEndpoint = "https://auth.example.com/authorize";
        var handler = new FakeHttpHandler("{}", HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);

        using var client = new OAuthClient(settings, httpClient);
        var act = () => client.BuildAuthorizationUrl(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task RequestDeviceAuthorizationAsync_ThrowsWithoutEndpoint()
    {
        var settings = CreateSettings(OAuthGrantType.DeviceCode);
        var handler = new FakeHttpHandler("{}", HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);

        using var client = new OAuthClient(settings, httpClient);
        var act = () => client.RequestDeviceAuthorizationAsync();
        await act.Should().ThrowAsync<OAuthException>().WithMessage("*DeviceAuthorizationEndpoint*");
    }

    [Fact]
    public async Task RequestDeviceAuthorizationAsync_ReturnsResponse()
    {
        var deviceResponse = JsonSerializer.Serialize(new
        {
            device_code = "dev-code-123",
            user_code = "ABCD-1234",
            verification_uri = "https://auth.example.com/device",
            verification_uri_complete = "https://auth.example.com/device?user_code=ABCD-1234",
            expires_in = 600,
            interval = 5
        });

        var handler = new FakeHttpHandler(deviceResponse, HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var settings = CreateSettings(OAuthGrantType.DeviceCode);
        settings.DeviceAuthorizationEndpoint = "https://auth.example.com/device/code";

        using var client = new OAuthClient(settings, httpClient);
        var response = await client.RequestDeviceAuthorizationAsync();

        response.DeviceCode.Should().Be("dev-code-123");
        response.UserCode.Should().Be("ABCD-1234");
        response.VerificationUri.Should().Be("https://auth.example.com/device");
        response.VerificationUriComplete.Should().Be("https://auth.example.com/device?user_code=ABCD-1234");
        response.ExpiresInSeconds.Should().Be(600);
        response.IntervalSeconds.Should().Be(5);
    }

    [Fact]
    public async Task RefreshTokenAsync_ClientCredentials_RequestsNewToken()
    {
        var tokenResponse = JsonSerializer.Serialize(new
        {
            access_token = "fresh-token",
            expires_in = 3600
        });

        var handler = new FakeHttpHandler(tokenResponse, HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var settings = CreateSettings();

        using var client = new OAuthClient(settings, httpClient);
        var token = await client.RefreshTokenAsync();

        token.AccessToken.Should().Be("fresh-token");
    }

    [Fact]
    public async Task ClearTokenCache_RemovesCachedToken()
    {
        var tokenResponse = JsonSerializer.Serialize(new
        {
            access_token = "token-1",
            expires_in = 3600
        });

        var handler = new FakeHttpHandler(tokenResponse, HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var settings = CreateSettings();

        using var client = new OAuthClient(settings, httpClient);

        // Get initial token
        var token1 = await client.GetTokenAsync();
        handler.RequestCount.Should().Be(1);

        // Clear cache and get again — should make another request
        client.ClearTokenCache();

        // Update response for second call
        handler.ResponseBody = JsonSerializer.Serialize(new
        {
            access_token = "token-2",
            expires_in = 3600
        });

        var token2 = await client.GetTokenAsync();
        handler.RequestCount.Should().Be(2);
        token2.AccessToken.Should().Be("token-2");
    }

    [Fact]
    public void Dispose_DisposesOwnedHttpClient()
    {
        var settings = CreateSettings();
        var client = new OAuthClient(settings);
        client.Dispose();
        // No exception means successful disposal
    }

    [Fact]
    public void Dispose_DoesNotDisposeInjectedHttpClient()
    {
        var settings = CreateSettings();
        var httpClient = new HttpClient();
        var client = new OAuthClient(settings, httpClient);
        client.Dispose();

        // Injected HttpClient should still be usable (not disposed)
        // Attempting to set a header verifies it's not disposed
        httpClient.DefaultRequestHeaders.Add("X-Test", "value");
        httpClient.Dispose();
    }

    // ---- CR-M058: PollDeviceTokenAsync coverage ----

    [Fact]
    public async Task PollDeviceTokenAsync_PollsPendingThenReturnsToken()
    {
        var handler = new SequencedHttpHandler(
            (HttpStatusCode.BadRequest, JsonSerializer.Serialize(new { error = "authorization_pending" })),
            (HttpStatusCode.OK, JsonSerializer.Serialize(new { access_token = "device-token", expires_in = 3600 })));
        var httpClient = new HttpClient(handler);
        var settings = CreateSettings(OAuthGrantType.DeviceCode);

        using var client = new OAuthClient(settings, httpClient);
        // interval 0 keeps the poll delay effectively zero so the test is fast
        var token = await client.PollDeviceTokenAsync("dev-code-123", intervalSeconds: 0);

        token.AccessToken.Should().Be("device-token");
        handler.RequestCount.Should().Be(2); // one pending poll, then the success
        handler.RequestBodies[0].Should().Contain("grant_type=urn%3Aietf%3Aparams%3Aoauth%3Agrant-type%3Adevice_code");
        handler.RequestBodies[0].Should().Contain("device_code=dev-code-123");
    }

    [Fact]
    public async Task PollDeviceTokenAsync_SlowDown_BumpsPollInterval()
    {
        var handler = new SequencedHttpHandler(
            (HttpStatusCode.BadRequest, JsonSerializer.Serialize(new { error = "slow_down" })),
            (HttpStatusCode.OK, JsonSerializer.Serialize(new { access_token = "after-slowdown", expires_in = 3600 })));
        var httpClient = new HttpClient(handler);
        var settings = CreateSettings(OAuthGrantType.DeviceCode);

        using var client = new OAuthClient(settings, httpClient);
        // start at 0; slow_down bumps the interval by 5s, so the second poll must be ~5s later
        var token = await client.PollDeviceTokenAsync("dev-code-123", intervalSeconds: 0);

        token.AccessToken.Should().Be("after-slowdown");
        handler.RequestCount.Should().Be(2);
        var gap = handler.RequestTimesUtc[1] - handler.RequestTimesUtc[0];
        gap.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(4)); // interval was bumped from 0 to 5
    }

    [Fact]
    public async Task PollDeviceTokenAsync_ServerExpiredToken_Throws()
    {
        var handler = new SequencedHttpHandler(
            (HttpStatusCode.BadRequest, JsonSerializer.Serialize(new
            {
                error = "expired_token",
                error_description = "The device code has expired."
            })));
        var httpClient = new HttpClient(handler);
        var settings = CreateSettings(OAuthGrantType.DeviceCode);

        using var client = new OAuthClient(settings, httpClient);
        var act = () => client.PollDeviceTokenAsync("dev-code-123", intervalSeconds: 0);

        var ex = await act.Should().ThrowAsync<OAuthException>();
        ex.Which.ErrorCode.Should().Be("expired_token");
        ex.Which.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task PollDeviceTokenAsync_Timeout_ThrowsExpiredToken()
    {
        // A zero timeout makes the poll deadline already elapsed → the loop exits and surfaces expired_token.
        var handler = new SequencedHttpHandler(
            (HttpStatusCode.BadRequest, JsonSerializer.Serialize(new { error = "authorization_pending" })));
        var httpClient = new HttpClient(handler);
        var settings = CreateSettings(OAuthGrantType.DeviceCode);
        settings.DeviceCodeTimeoutSeconds = 0;

        using var client = new OAuthClient(settings, httpClient);
        var act = () => client.PollDeviceTokenAsync("dev-code-123", intervalSeconds: 0);

        var ex = await act.Should().ThrowAsync<OAuthException>().WithMessage("*timed out*");
        ex.Which.ErrorCode.Should().Be("expired_token");
        handler.RequestCount.Should().Be(0); // deadline already passed, no poll issued
    }

    // ---- CR-M058: RefreshTokenAsync real refresh_token exchange path ----

    [Fact]
    public async Task RefreshTokenAsync_WithCachedRefreshToken_SendsRefreshGrant()
    {
        var handler = new SequencedHttpHandler(
            // first: seed a cached token that carries a real refresh_token
            (HttpStatusCode.OK, JsonSerializer.Serialize(new
            {
                access_token = "initial-token",
                expires_in = 3600,
                refresh_token = "rt-abc-123"
            })),
            // second: the refreshed token
            (HttpStatusCode.OK, JsonSerializer.Serialize(new
            {
                access_token = "refreshed-token",
                expires_in = 3600,
                refresh_token = "rt-def-456"
            })));
        var httpClient = new HttpClient(handler);
        var settings = CreateSettings(); // ClientCredentials, but cached refresh_token wins

        using var client = new OAuthClient(settings, httpClient);
        await client.GetTokenAsync();          // caches initial-token (+ refresh_token)
        var token = await client.RefreshTokenAsync();

        token.AccessToken.Should().Be("refreshed-token");
        handler.RequestCount.Should().Be(2);
        handler.RequestBodies[1].Should().Contain("grant_type=refresh_token");
        handler.RequestBodies[1].Should().Contain("refresh_token=rt-abc-123");
    }

    [Fact]
    public async Task GetTokenAsync_RefreshFailure_FallsBackToPrimaryFlow()
    {
        var handler = new SequencedHttpHandler(
            // first: seed an already-expired token that carries a refresh_token
            (HttpStatusCode.OK, JsonSerializer.Serialize(new
            {
                access_token = "stale-token",
                expires_in = 0,
                refresh_token = "rt-expired"
            })),
            // second: the refresh attempt fails
            (HttpStatusCode.BadRequest, JsonSerializer.Serialize(new
            {
                error = "invalid_grant",
                error_description = "Refresh token expired"
            })),
            // third: fallback client_credentials succeeds
            (HttpStatusCode.OK, JsonSerializer.Serialize(new { access_token = "fallback-token", expires_in = 3600 })));
        var httpClient = new HttpClient(handler);
        var settings = CreateSettings(); // ClientCredentials

        using var client = new OAuthClient(settings, httpClient);
        await client.GetTokenAsync();          // caches the expired token (req #1)
        var token = await client.GetTokenAsync(); // expired → tries refresh (req #2, fails) → falls back (req #3)

        token.AccessToken.Should().Be("fallback-token");
        handler.RequestCount.Should().Be(3);
        handler.RequestBodies[1].Should().Contain("grant_type=refresh_token"); // the failed refresh attempt
        handler.RequestBodies[2].Should().Contain("grant_type=client_credentials"); // the fallback
    }

    // ---- CR-M058: ExchangeCodeAsync confidential-client branch ----

    [Fact]
    public async Task ExchangeCodeAsync_ConfidentialClient_SendsSecretNotVerifier()
    {
        var tokenResponse = JsonSerializer.Serialize(new { access_token = "confidential-token", expires_in = 3600 });
        var handler = new FakeHttpHandler(tokenResponse, HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var settings = CreateSettings(OAuthGrantType.AuthorizationCode); // has ClientSecret

        using var client = new OAuthClient(settings, httpClient);
        var token = await client.ExchangeCodeAsync("auth-code-xyz"); // no codeVerifier

        token.AccessToken.Should().Be("confidential-token");
        handler.LastRequestBody.Should().Contain("grant_type=authorization_code");
        handler.LastRequestBody.Should().Contain("client_secret=test-secret");
        handler.LastRequestBody.Should().NotContain("code_verifier");
    }

    // ---- CR-M058: OAuthDelegatingHandler 401 retry through a real resend ----

    [Fact]
    public async Task OAuthDelegatingHandler_On401_ResendsClonedRequestWithFreshToken()
    {
        // The OAuth client hands out two distinct tokens on successive calls (client_credentials each time).
        var tokenHandler = new SequencedHttpHandler(
            (HttpStatusCode.OK, JsonSerializer.Serialize(new { access_token = "tok-1", expires_in = 3600 })),
            (HttpStatusCode.OK, JsonSerializer.Serialize(new { access_token = "tok-2", expires_in = 3600 })));
        var tokenClient = new HttpClient(tokenHandler);
        using var oauth = new OAuthClient(CreateSettings(), tokenClient);

        // The resource server rejects the first (stale) token with 401 and accepts the refreshed one.
        var resource = new InspectingHttpHandler((req, body) =>
        {
            var auth = req.Headers.Authorization?.ToString();
            var status = auth == "Bearer tok-1" ? HttpStatusCode.Unauthorized : HttpStatusCode.OK;
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(status == HttpStatusCode.OK ? "resource-ok" : "unauthorized")
            };
        });

        var delegating = new OAuthDelegatingHandler(oauth, resource);
        using var apiClient = new HttpClient(delegating);

        using var response = await apiClient.PostAsync(
            "https://api.example.com/data", new StringContent("payload-body"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("resource-ok");

        resource.Requests.Should().HaveCount(2);
        resource.AuthHeaders[0].Should().Be("Bearer tok-1"); // original send, rejected
        resource.AuthHeaders[1].Should().Be("Bearer tok-2"); // retry with the refreshed token
        resource.Requests[0].Should().NotBeSameAs(resource.Requests[1]); // retry is a genuine clone, not the sent instance
        resource.Bodies[0].Should().Be("payload-body");
        resource.Bodies[1].Should().Be("payload-body"); // buffered body survives the resend
    }

    #region Test Helpers

    private class FakeHttpHandler : HttpMessageHandler
    {
        public string ResponseBody { get; set; }
        public HttpStatusCode StatusCode { get; set; }
        public int RequestCount { get; private set; }
        public string? LastRequestBody { get; private set; }

        public FakeHttpHandler(string responseBody, HttpStatusCode statusCode)
        {
            ResponseBody = responseBody;
            StatusCode = statusCode;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;

            if (request.Content != null)
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            return new HttpResponseMessage(StatusCode)
            {
                Content = new StringContent(ResponseBody, System.Text.Encoding.UTF8, "application/json")
            };
        }
    }

    /// <summary>
    /// Returns a queued sequence of responses in order (repeating the last one if exhausted),
    /// recording each request's body and the UTC time it was received.
    /// </summary>
    private sealed class SequencedHttpHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body)> _responses;
        private readonly (HttpStatusCode Status, string Body) _last;

        public int RequestCount { get; private set; }
        public List<string?> RequestBodies { get; } = new();
        public List<DateTime> RequestTimesUtc { get; } = new();

        public SequencedHttpHandler(params (HttpStatusCode Status, string Body)[] responses)
        {
            _responses = new Queue<(HttpStatusCode, string)>(responses);
            _last = responses[responses.Length - 1];
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            RequestTimesUtc.Add(DateTime.UtcNow);
            RequestBodies.Add(request.Content != null
                ? await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)
                : null);

            var (status, body) = _responses.Count > 0 ? _responses.Dequeue() : _last;
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            };
        }
    }

    /// <summary>
    /// Delegates the response decision to a supplied responder while recording every request instance,
    /// its Authorization header, and its body — so a resend/clone can be inspected against the real one.
    /// </summary>
    private sealed class InspectingHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, string?, HttpResponseMessage> _responder;

        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string?> AuthHeaders { get; } = new();
        public List<string?> Bodies { get; } = new();

        public InspectingHttpHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> responder)
            => _responder = responder;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            AuthHeaders.Add(request.Headers.Authorization?.ToString());
            var body = request.Content != null
                ? await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)
                : null;
            Bodies.Add(body);
            return _responder(request, body);
        }
    }

    #endregion
}
