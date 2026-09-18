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

    [Fact]
    public async Task SendAsync_RetriesOn401_WithClonedRequestAndPreservedBody()
    {
        // Regression for CR-H030: the retry used to resend the SAME HttpRequestMessage, which real
        // handlers reject ('already sent') and whose body stream is consumed. Assert the retry gets
        // a DISTINCT instance and the POST body survives.
        var tokenResponse = JsonSerializer.Serialize(new { access_token = "fresh-token", expires_in = 3600 });
        using var oauthClient = new OAuthClient(
            new OAuthSettings { TokenEndpoint = "https://auth.example.com/token", ClientId = "c", ClientSecret = "s" },
            new HttpClient(new FakeHttpHandler(tokenResponse, HttpStatusCode.OK)));

        HttpRequestMessage? firstInstance = null;
        var bodies = new System.Collections.Generic.List<string>();
        var apiHandler = new InspectingHandler(async (req, ct) =>
        {
            // Mimic the real pipeline: the same instance may not be sent twice.
            if (firstInstance is not null && ReferenceEquals(firstInstance, req))
                throw new InvalidOperationException("The request message was already sent.");

            bodies.Add(req.Content is null ? "" : await req.Content.ReadAsStringAsync(ct));

            if (firstInstance is null)
            {
                firstInstance = req;
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var handler = new OAuthDelegatingHandler(oauthClient, apiHandler);
        using var httpClient = new HttpClient(handler);

        var response = await httpClient.PostAsync("https://api.example.com/data",
            new StringContent("payload-body", System.Text.Encoding.UTF8, "text/plain"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        bodies.Should().HaveCount(2);
        bodies.Should().OnlyContain(b => b == "payload-body", "the buffered body must survive the retry");
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
