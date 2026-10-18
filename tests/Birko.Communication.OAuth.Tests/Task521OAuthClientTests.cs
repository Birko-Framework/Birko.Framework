using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace Birko.Communication.OAuth.Tests;

/// <summary>
/// TASK-521, found while backporting DraCode's GitHub web sign-in. GitHub's token endpoint (web flow and device flow
/// alike) answers an error with HTTP <b>200</b> and a JSON <c>error</c> body, and answers form-encoded unless the request
/// asks for JSON. <see cref="OAuthClient"/> treated every 2xx as a token, and a borrowed <see cref="HttpClient"/> had
/// its <c>Timeout</c> set in the constructor, which throws once that client has sent a request.
/// </summary>
public class Task521OAuthClientTests
{
    /// <summary>Answers each request with the next queued (status, body), and records each request's Accept header.</summary>
    private sealed class QueueHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body)> _responses = new();
        public List<string> Accepts { get; } = new();

        public QueueHandler(params (HttpStatusCode, string)[] responses)
        {
            foreach (var r in responses) _responses.Enqueue(r);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Accepts.Add(request.Headers.Accept.ToString());
            var (status, body) = _responses.Count > 1 ? _responses.Dequeue() : _responses.Peek();
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }

    private static OAuthSettings Settings(OAuthGrantType grant) => new()
    {
        GrantType = grant,
        TokenEndpoint = "https://github.com/login/oauth/access_token",
        ClientId = "id",
        ClientSecret = "secret",
        DeviceCodePollingIntervalSeconds = 0,
        DeviceCodeTimeoutSeconds = 30,
    };

    [Fact]
    public async Task A_200_error_body_from_the_code_exchange_is_an_OAuthException()
    {
        var handler = new QueueHandler((HttpStatusCode.OK,
            """{"error":"bad_verification_code","error_description":"The code passed is incorrect or expired."}"""));
        using var client = new OAuthClient(Settings(OAuthGrantType.AuthorizationCode), new HttpClient(handler));

        var ex = await FluentActions.Awaiting(() => client.ExchangeCodeAsync("stale"))
            .Should().ThrowAsync<OAuthException>();
        ex.Which.ErrorCode.Should().Be("bad_verification_code");
        ex.Which.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task Device_polling_keeps_going_through_a_200_authorization_pending()
    {
        var handler = new QueueHandler(
            (HttpStatusCode.OK, """{"error":"authorization_pending"}"""),
            (HttpStatusCode.OK, """{"access_token":"gho_x","token_type":"bearer","scope":"read:user"}"""));
        using var client = new OAuthClient(Settings(OAuthGrantType.DeviceCode), new HttpClient(handler));

        var token = await client.PollDeviceTokenAsync("device-code", intervalSeconds: 0);

        token.AccessToken.Should().Be("gho_x");
    }

    [Fact]
    public async Task Token_requests_ask_for_JSON()
    {
        var handler = new QueueHandler((HttpStatusCode.OK, """{"access_token":"t"}"""));
        using var client = new OAuthClient(Settings(OAuthGrantType.AuthorizationCode), new HttpClient(handler));

        await client.ExchangeCodeAsync("code");

        handler.Accepts.Should().ContainSingle().Which.Should().Contain("application/json",
            "GitHub answers form-encoded otherwise, which the JSON parser cannot read");
    }

    [Fact]
    public async Task A_borrowed_HttpClient_that_has_already_sent_a_request_can_back_another_client()
    {
        var handler = new QueueHandler((HttpStatusCode.OK, """{"access_token":"t"}"""));
        var http = new HttpClient(handler);
        using (var first = new OAuthClient(Settings(OAuthGrantType.AuthorizationCode), http))
            await first.ExchangeCodeAsync("one");

        var second = () => new OAuthClient(Settings(OAuthGrantType.AuthorizationCode), http);

        using var client = second.Should().NotThrow("a client it does not own must not be reconfigured").Subject;
        (await client.ExchangeCodeAsync("two")).AccessToken.Should().Be("t");
        http.Timeout.Should().Be(TimeSpan.FromSeconds(100), "the caller's HttpClient keeps its own timeout");
    }

    [Fact]
    public async Task A_borrowed_HttpClient_still_gets_the_settings_timeout_per_request()
    {
        var http = new HttpClient(new HangingHandler());
        var settings = Settings(OAuthGrantType.AuthorizationCode);
        settings.TimeoutSeconds = 1;
        using var client = new OAuthClient(settings, http);

        var started = DateTime.UtcNow;
        await FluentActions.Awaiting(() => client.ExchangeCodeAsync("code")).Should().ThrowAsync<OperationCanceledException>();
        (DateTime.UtcNow - started).Should().BeLessThan(TimeSpan.FromSeconds(10));
    }

    private sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
