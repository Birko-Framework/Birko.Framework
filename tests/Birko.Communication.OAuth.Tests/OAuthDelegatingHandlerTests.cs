using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace Birko.Communication.OAuth.Tests;

public class OAuthDelegatingHandlerTests
{
    [Fact]
    public void Constructor_ThrowsOnNull()
    {
        var act = () => new OAuthDelegatingHandler(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task SendAsync_AttachesBearerToken()
    {
        var tokenResponse = JsonSerializer.Serialize(new { access_token = "my-token", expires_in = 3600 });
        var oauthHandler = new FakeHttpHandler(tokenResponse, HttpStatusCode.OK);
        var oauthHttpClient = new HttpClient(oauthHandler);

        var settings = new OAuthSettings
        {
            TokenEndpoint = "https://auth.example.com/token",
            ClientId = "client",
            ClientSecret = "secret"
        };

        using var oauthClient = new OAuthClient(settings, oauthHttpClient);

        string? capturedAuthHeader = null;
        var apiHandler = new InspectingHandler((req, _) =>
        {
            capturedAuthHeader = req.Headers.Authorization?.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        using var handler = new OAuthDelegatingHandler(oauthClient, apiHandler);
        using var httpClient = new HttpClient(handler);

        await httpClient.GetAsync("https://api.example.com/data");

        capturedAuthHeader.Should().Be("Bearer my-token");
    }

    [Fact]
    public async Task SendAsync_RetriesOn401WithFreshToken()
    {
        var tokenResponse = JsonSerializer.Serialize(new { access_token = "fresh-token", expires_in = 3600 });
        var oauthHandler = new FakeHttpHandler(tokenResponse, HttpStatusCode.OK);
        var oauthHttpClient = new HttpClient(oauthHandler);

        var settings = new OAuthSettings
        {
            TokenEndpoint = "https://auth.example.com/token",
            ClientId = "client",
            ClientSecret = "secret"
        };

        using var oauthClient = new OAuthClient(settings, oauthHttpClient);

        int callCount = 0;
        var apiHandler = new InspectingHandler((req, _) =>
        {
            callCount++;
            if (callCount == 1)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        using var handler = new OAuthDelegatingHandler(oauthClient, apiHandler);
        using var httpClient = new HttpClient(handler);

        var response = await httpClient.GetAsync("https://api.example.com/data");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        callCount.Should().Be(2); // Initial + retry
    }

    #region Test Helpers

    private class FakeHttpHandler : HttpMessageHandler
    {
        private readonly string _responseBody;
        private readonly HttpStatusCode _statusCode;

        public FakeHttpHandler(string responseBody, HttpStatusCode statusCode)
        {
            _responseBody = responseBody;
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseBody, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }

    private class InspectingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public InspectingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _handler(request, cancellationToken);
        }
    }

    #endregion
}
