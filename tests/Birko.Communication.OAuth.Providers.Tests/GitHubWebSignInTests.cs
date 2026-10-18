using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Birko.Communication.OAuth;
using Birko.Communication.OAuth.Providers;
using FluentAssertions;
using Xunit;

namespace Birko.Communication.OAuth.Providers.Tests;

/// <summary>
/// TASK-521: GitHub web (authorization-code) sign-in — <see cref="GitHubOAuthProvider.CreateWebFlowSettings"/> and
/// <see cref="GitHubUserClient"/>, backported from DraCode's GitHubTokenExchanger and GitHubUserInfoClient.
/// </summary>
public class GitHubWebSignInTests
{
    private sealed class FakeGitHub : HttpMessageHandler
    {
        public Dictionary<string, (HttpStatusCode Status, string Body)> Routes { get; } = new();
        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            var (status, body) = Routes.TryGetValue(request.RequestUri!.AbsoluteUri, out var r) ? r : (HttpStatusCode.NotFound, "{}");
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    [Fact]
    public void Web_flow_settings_build_GitHubs_authorization_url()
    {
        var settings = GitHubOAuthProvider.CreateWebFlowSettings("cid", "secret", "https://app.test/auth/github/callback");
        using var client = new OAuthClient(settings);

        var url = new Uri(client.BuildAuthorizationUrl("state-1"));

        url.GetLeftPart(UriPartial.Path).Should().Be(GitHubOAuthProvider.AuthorizationEndpoint);
        url.Query.Should().Contain("client_id=cid").And.Contain("state=state-1").And.Contain("scope=read%3Auser%20user%3Aemail")
            .And.Contain("redirect_uri=https%3A%2F%2Fapp.test%2Fauth%2Fgithub%2Fcallback");
        settings.TokenEndpoint.Should().Be(GitHubOAuthProvider.TokenEndpoint);
        settings.GrantType.Should().Be(OAuthGrantType.AuthorizationCode);
    }

    [Theory]
    [InlineData("", "s", "https://x")]
    [InlineData("c", "", "https://x")]
    [InlineData("c", "s", "")]
    public void Web_flow_settings_need_id_secret_and_redirect(string id, string secret, string redirect)
    {
        FluentActions.Invoking(() => GitHubOAuthProvider.CreateWebFlowSettings(id, secret, redirect)).Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task The_user_is_read_with_the_token_and_GitHubs_required_headers()
    {
        var github = new FakeGitHub();
        github.Routes["https://api.github.com/user"] = (HttpStatusCode.OK,
            """{"id":583231,"login":"octocat","name":"The Octocat","email":null}""");
        var http = new HttpClient(github);
        var client = new GitHubUserClient(http, "MyApp");

        var user = await client.GetUserAsync("gho_token");
        await client.GetUserAsync("gho_token");

        user.Should().Be(new GitHubUser(583231, "octocat", "The Octocat", null));
        var request = github.Requests[0];
        request.Headers.Authorization!.ToString().Should().Be("Bearer gho_token");
        request.Headers.UserAgent.ToString().Should().Be("MyApp", "GitHub answers 403 without a User-Agent");
        request.Headers.Accept.ToString().Should().Be("application/vnd.github+json");
        github.Requests[1].Headers.Accept.Should().HaveCount(1, "headers are per request, so nothing accumulates");
        http.DefaultRequestHeaders.Should().BeEmpty("a shared HttpClient is not modified");
    }

    [Fact]
    public async Task A_refused_token_is_an_OAuthException_with_GitHubs_status_and_message()
    {
        var github = new FakeGitHub();
        github.Routes["https://api.github.com/user"] = (HttpStatusCode.Unauthorized, """{"message":"Bad credentials"}""");

        var ex = await FluentActions.Awaiting(() => new GitHubUserClient(new HttpClient(github)).GetUserAsync("bad"))
            .Should().ThrowAsync<OAuthException>();
        ex.Which.StatusCode.Should().Be(401);
        ex.Which.ErrorDescription.Should().Be("Bad credentials");
    }

    [Theory]
    [InlineData("""{"login":"octocat"}""")]
    [InlineData("""{"id":"583231"}""")]
    [InlineData("[]")]
    [InlineData("not json")]
    public async Task A_user_response_without_a_numeric_id_is_refused(string body)
    {
        var github = new FakeGitHub();
        github.Routes["https://api.github.com/user"] = (HttpStatusCode.OK, body);

        await FluentActions.Awaiting(() => new GitHubUserClient(new HttpClient(github)).GetUserAsync("t"))
            .Should().ThrowAsync<OAuthException>("the id is the account key; a user without one cannot be signed in");
    }

    [Theory]
    [InlineData("""[{"email":"old@x.io","primary":false,"verified":true},{"email":"me@x.io","primary":true,"verified":true}]""", "me@x.io")]
    [InlineData("""[{"email":"me@x.io","primary":true,"verified":false}]""", null)]
    [InlineData("""[{"email":"other@x.io","primary":false,"verified":true}]""", null)]
    [InlineData("[]", null)]
    public async Task The_primary_email_is_returned_only_when_verified(string body, string? expected)
    {
        var github = new FakeGitHub();
        github.Routes["https://api.github.com/user/emails"] = (HttpStatusCode.OK, body);

        (await new GitHubUserClient(new HttpClient(github)).GetPrimaryEmailAsync("t")).Should().Be(expected);
    }

    [Fact]
    public async Task An_enterprise_api_base_is_honoured()
    {
        var github = new FakeGitHub();
        github.Routes["https://ghe.example.com/api/v3/user"] = (HttpStatusCode.OK, """{"id":1,"login":"a"}""");

        var user = await new GitHubUserClient(new HttpClient(github), apiBase: "https://ghe.example.com/api/v3").GetUserAsync("t");

        user.Id.Should().Be(1);
    }

    [Fact]
    public async Task A_full_sign_in_runs_against_fakes_end_to_end()
    {
        var github = new FakeGitHub();
        github.Routes[GitHubOAuthProvider.TokenEndpoint] = (HttpStatusCode.OK, """{"access_token":"gho_abc","token_type":"bearer","scope":"read:user,user:email"}""");
        github.Routes["https://api.github.com/user"] = (HttpStatusCode.OK, """{"id":42,"login":"octocat","name":null,"email":null}""");
        github.Routes["https://api.github.com/user/emails"] = (HttpStatusCode.OK, """[{"email":"o@x.io","primary":true,"verified":true}]""");
        var http = new HttpClient(github);
        var settings = GitHubOAuthProvider.CreateWebFlowSettings("cid", "secret", "https://app.test/cb");
        var states = new InMemoryOAuthStateStore();
        IOAuthCodeExchanger exchanger = new OAuthCodeExchanger(settings, http);
        IGitHubUserClient users = new GitHubUserClient(http);

        // sign-in start: a state bound to the browser's cookie
        var state = await states.CreateAsync("browser-cookie");
        using (var client = new OAuthClient(settings)) client.BuildAuthorizationUrl(state).Should().Contain(state);

        // callback
        (await states.TryConsumeAsync(state, "browser-cookie")).Should().NotBeNull();
        var token = await exchanger.ExchangeAsync("the-code");
        var user = await users.GetUserAsync(token.AccessToken);
        var email = user.Email ?? await users.GetPrimaryEmailAsync(token.AccessToken);

        (user.Id, user.Login, email).Should().Be((42L, "octocat", "o@x.io"));
    }
}
