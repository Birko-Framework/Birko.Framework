using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace Birko.Communication.OAuth.Tests;

/// <summary>
/// TASK-521: <see cref="InMemoryOAuthStateStore"/> (single-use, expiring, browser-bound sign-in state) and
/// <see cref="OAuthCodeExchanger"/>, backported from DraCode's OAuthStateStore and GitHubTokenExchanger.
/// </summary>
public class OAuthStateStoreTests
{
    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task A_state_round_trips_with_its_data_once()
    {
        var store = new InMemoryOAuthStateStore();
        var state = await store.CreateAsync("cookie-1", "verifier-abc");

        (await store.TryConsumeAsync(state, "cookie-1"))!.Data.Should().Be("verifier-abc");
        (await store.TryConsumeAsync(state, "cookie-1")).Should().BeNull("a state is single-use");
    }

    [Fact]
    public async Task States_are_unique_and_url_safe()
    {
        var store = new InMemoryOAuthStateStore();
        var states = await Task.WhenAll(Enumerable.Range(0, 100).Select(_ => store.CreateAsync(null)));

        states.Should().OnlyHaveUniqueItems();
        states.Should().AllSatisfy(s => s.Should().MatchRegex("^[A-Za-z0-9_-]{43}$"));
    }

    [Fact]
    public async Task An_expired_state_is_refused()
    {
        var clock = new ManualClock();
        var store = new InMemoryOAuthStateStore(new OAuthStateStoreOptions { Lifetime = TimeSpan.FromMinutes(5) }, clock);
        var fresh = await store.CreateAsync("b");
        var stale = await store.CreateAsync("b");

        clock.Now += TimeSpan.FromMinutes(4);
        (await store.TryConsumeAsync(fresh, "b")).Should().NotBeNull();
        clock.Now += TimeSpan.FromMinutes(1);
        (await store.TryConsumeAsync(stale, "b")).Should().BeNull("its lifetime ended");
    }

    [Theory]
    [InlineData("victim-cookie")]
    [InlineData(null)]
    public async Task A_state_presented_by_another_browser_is_refused_and_spent(string? presented)
    {
        var store = new InMemoryOAuthStateStore();
        var state = await store.CreateAsync("attacker-cookie");

        (await store.TryConsumeAsync(state, presented)).Should().BeNull("login CSRF: the state belongs to the browser that started the sign-in");
        (await store.TryConsumeAsync(state, "attacker-cookie")).Should().BeNull("a refused presentation spends the state");
    }

    [Fact]
    public async Task An_unbound_state_needs_no_binding_and_refuses_one()
    {
        var store = new InMemoryOAuthStateStore();
        (await store.TryConsumeAsync(await store.CreateAsync(null), null)).Should().NotBeNull();
        (await store.TryConsumeAsync(await store.CreateAsync(null), "cookie")).Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("never-issued")]
    public async Task An_unknown_state_is_refused(string? state)
    {
        (await new InMemoryOAuthStateStore().TryConsumeAsync(state, null)).Should().BeNull();
    }

    [Fact]
    public async Task Concurrent_callbacks_with_one_state_succeed_once()
    {
        var store = new InMemoryOAuthStateStore();
        for (var round = 0; round < 20; round++)
        {
            var state = await store.CreateAsync("b");
            var results = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() => store.TryConsumeAsync(state, "b"))));
            results.Count(r => r != null).Should().Be(1);
        }
    }

    [Fact]
    public async Task The_store_is_bounded_sweeping_expired_states_first()
    {
        var clock = new ManualClock();
        var store = new InMemoryOAuthStateStore(new OAuthStateStoreOptions { MaxPending = 3, Lifetime = TimeSpan.FromMinutes(1) }, clock);
        var expired = await store.CreateAsync(null);
        clock.Now += TimeSpan.FromMinutes(2);
        var a = await store.CreateAsync(null);
        clock.Now += TimeSpan.FromSeconds(1);
        var b = await store.CreateAsync(null);
        clock.Now += TimeSpan.FromSeconds(1);

        var c = await store.CreateAsync(null); // full: the expired one is swept, nothing live is evicted
        store.Count.Should().Be(3);
        (await store.TryConsumeAsync(a, null)).Should().NotBeNull();

        clock.Now += TimeSpan.FromSeconds(1);
        var d = await store.CreateAsync(null);
        clock.Now += TimeSpan.FromSeconds(1);
        var e = await store.CreateAsync(null); // full of live states: the oldest (b) is evicted
        store.Count.Should().BeLessThanOrEqualTo(3);
        (await store.TryConsumeAsync(b, null)).Should().BeNull();
        (await store.TryConsumeAsync(e, null)).Should().NotBeNull();
        (await store.TryConsumeAsync(expired, null)).Should().BeNull();
        _ = (c, d);
    }

    [Fact]
    public void Invalid_options_are_refused()
    {
        FluentActions.Invoking(() => new InMemoryOAuthStateStore(new OAuthStateStoreOptions { Lifetime = TimeSpan.Zero }))
            .Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => new InMemoryOAuthStateStore(new OAuthStateStoreOptions { MaxPending = 0 }))
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    // ── OAuthCodeExchanger ──

    private sealed class RecordingHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public List<string> Bodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Bodies.Add(request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct));
            return new HttpResponseMessage(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
        }
    }

    private static OAuthSettings CodeSettings() => new()
    {
        GrantType = OAuthGrantType.AuthorizationCode,
        TokenEndpoint = "https://github.com/login/oauth/access_token",
        ClientId = "id",
        ClientSecret = "secret",
        RedirectUri = "https://app.test/callback",
    };

    [Fact]
    public async Task The_exchanger_can_be_a_singleton_over_one_HttpClient()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"access_token":"gho_1","token_type":"bearer"}""");
        var exchanger = new OAuthCodeExchanger(CodeSettings(), new HttpClient(handler));

        (await exchanger.ExchangeAsync("c1")).AccessToken.Should().Be("gho_1");
        (await exchanger.ExchangeAsync("c2", "verifier")).AccessToken.Should().Be("gho_1", "a second sign-in reuses the HttpClient");

        handler.Bodies[0].Should().Contain("code=c1").And.Contain("client_secret=secret").And.Contain("redirect_uri=");
        handler.Bodies[1].Should().Contain("code_verifier=verifier");
    }

    [Fact]
    public async Task A_refused_code_is_an_OAuthException()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"error":"bad_verification_code","error_description":"expired"}""");
        var exchanger = new OAuthCodeExchanger(CodeSettings(), new HttpClient(handler));

        (await FluentActions.Awaiting(() => exchanger.ExchangeAsync("stale")).Should().ThrowAsync<OAuthException>())
            .Which.ErrorCode.Should().Be("bad_verification_code");
    }

    [Fact]
    public async Task An_empty_code_is_refused_before_any_request()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "{}");
        var exchanger = new OAuthCodeExchanger(CodeSettings(), new HttpClient(handler));

        await FluentActions.Awaiting(() => exchanger.ExchangeAsync("")).Should().ThrowAsync<ArgumentException>();
        handler.Bodies.Should().BeEmpty();
    }
}
