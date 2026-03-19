using System;
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

    #endregion
}
