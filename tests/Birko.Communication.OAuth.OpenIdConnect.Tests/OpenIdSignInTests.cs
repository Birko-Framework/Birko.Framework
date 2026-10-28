using System.Net;
using FluentAssertions;
using Xunit;

namespace Birko.Communication.OAuth.OpenIdConnect.Tests;

/// <summary>TASK-778: the sign-in round trip, and every way a callback is refused.</summary>
public class OpenIdSignInTests
{
    [Fact]
    public async Task The_authorization_url_carries_state_nonce_and_PKCE()
    {
        var h = new SignInHarness(o => o.AuthorizationParameters["prompt"] = "select_account");
        var (state, nonce, url) = await h.BeginAsync();
        var q = SignInHarness.Query(url);

        url.GetLeftPart(UriPartial.Path).Should().Be(FakeOpenIdProvider.Base + "/authorize");
        q["response_type"].Should().Be("code");
        q["client_id"].Should().Be(SignInHarness.ClientId);
        q["redirect_uri"].Should().Be("https://app.test/signin/callback");
        q["scope"].Should().Be("openid email profile");
        q["state"].Should().Be(state);
        nonce.Should().HaveLength(43, "32 random bytes, base64url");
        q["code_challenge_method"].Should().Be("S256");
        q["code_challenge"].Should().NotBeNullOrEmpty();
        q["prompt"].Should().Be("select_account");
        q.Should().NotContainKey("client_secret");
    }

    [Fact]
    public async Task A_valid_sign_in_returns_the_verified_identity_and_the_data()
    {
        var h = new SignInHarness();

        var result = await h.SignInWithAsync(h.Provider.Sign, data: "/decks/42");

        result.Status.Should().Be(OpenIdSignInStatus.Succeeded, result.Error);
        result.Data.Should().Be("/decks/42");
        result.Identity.Should().Be(new OpenIdIdentity("test", "user-1", FakeOpenIdProvider.Base, "ada@example.test", true, "Ada", null));
    }

    [Fact]
    public async Task The_code_exchange_sends_the_verifier_and_the_client_secret()
    {
        var h = new SignInHarness();
        await h.SignInWithAsync(h.Provider.Sign);

        var body = h.Provider.TokenRequests.Single();
        body.Should().Contain("grant_type=authorization_code").And.Contain("code=good").And.Contain("code_verifier=")
            .And.Contain("client_secret=secret-1").And.Contain("redirect_uri=");
        body.Should().NotContain("nonce", "the nonce goes only in the authorization request");
    }

    // ── the id_token ──

    [Fact]
    public async Task A_forged_signature_is_refused()
    {
        var h = new SignInHarness();
        var forger = FakeOpenIdProvider.NewKey("key-1"); // the provider's key id, another key

        var result = await h.SignInWithClaimsAsync(_ => { }, forger);

        result.Status.Should().Be(OpenIdSignInStatus.InvalidIdToken);
        result.Identity.Should().BeNull();
    }

    [Fact]
    public async Task A_token_with_alg_none_is_refused()
    {
        var h = new SignInHarness();
        (await h.SignInWithAsync(FakeOpenIdProvider.Unsigned)).Status.Should().Be(OpenIdSignInStatus.InvalidIdToken);
    }

    [Fact]
    public async Task A_wrong_issuer_is_refused()
    {
        var h = new SignInHarness();
        var result = await h.SignInWithClaimsAsync(c => c["iss"] = "https://evil.test");
        result.Status.Should().Be(OpenIdSignInStatus.InvalidIdToken);
        result.Error.Should().Be("wrong issuer");
    }

    [Fact]
    public async Task A_wrong_audience_is_refused()
    {
        var h = new SignInHarness();
        (await h.SignInWithClaimsAsync(c => c["aud"] = "another-app")).Status.Should().Be(OpenIdSignInStatus.InvalidIdToken);
    }

    [Fact]
    public async Task Several_audiences_need_azp_naming_this_client()
    {
        var h = new SignInHarness();
        (await h.SignInWithClaimsAsync(c => c["aud"] = new[] { SignInHarness.ClientId, "other" }))
            .Error.Should().Be("several audiences and no azp");
        (await h.SignInWithClaimsAsync(c => { c["aud"] = new[] { SignInHarness.ClientId, "other" }; c["azp"] = "other"; }))
            .Error.Should().Be("azp is another client");
        (await h.SignInWithClaimsAsync(c => { c["aud"] = new[] { SignInHarness.ClientId, "other" }; c["azp"] = SignInHarness.ClientId; }))
            .Status.Should().Be(OpenIdSignInStatus.Succeeded);
    }

    [Fact]
    public async Task An_expired_token_is_refused_past_the_clock_skew()
    {
        var h = new SignInHarness();
        var now = h.Clock.Now.ToUnixTimeSeconds();

        (await h.SignInWithClaimsAsync(c => c["exp"] = now - 60)).Status
            .Should().Be(OpenIdSignInStatus.Succeeded, "a minute late is within the two-minute skew");
        var expired = await h.SignInWithClaimsAsync(c => c["exp"] = now - 180);
        expired.Status.Should().Be(OpenIdSignInStatus.InvalidIdToken);
        expired.Error.Should().Be("expired");
    }

    [Fact]
    public async Task A_token_without_exp_or_not_yet_valid_is_refused()
    {
        var h = new SignInHarness();
        (await h.SignInWithClaimsAsync(c => c.Remove("exp"))).Error.Should().Be("no exp");
        (await h.SignInWithClaimsAsync(c => c["nbf"] = h.Clock.Now.ToUnixTimeSeconds() + 600)).Error.Should().Be("not yet valid");
    }

    [Fact]
    public async Task A_wrong_or_missing_nonce_is_refused()
    {
        var h = new SignInHarness();
        (await h.SignInWithClaimsAsync(c => c["nonce"] = "another-sign-ins-nonce")).Error.Should().Be("nonce mismatch");
        (await h.SignInWithClaimsAsync(c => c.Remove("nonce"))).Error.Should().Be("nonce mismatch");
    }

    [Fact]
    public async Task A_token_without_sub_is_refused()
    {
        var h = new SignInHarness();
        (await h.SignInWithClaimsAsync(c => c.Remove("sub"))).Error.Should().Be("no sub");
    }

    [Fact]
    public async Task A_token_response_without_id_token_is_refused()
    {
        var h = new SignInHarness();
        var (state, _, _) = await h.BeginAsync();
        h.Provider.NextIdToken = null;

        var result = await h.SignIn.CompleteAsync(new OpenIdCallback(state, "good"), SignInHarness.Binding);

        result.Status.Should().Be(OpenIdSignInStatus.InvalidIdToken);
    }

    [Theory]
    [InlineData("email_verified", true, true)]
    [InlineData("email_verified", "true", true)]
    [InlineData("email_verified", false, false)]
    [InlineData("xms_edov", true, true)]
    [InlineData("xms_edov", "1", true)]
    [InlineData(null, null, false)]
    public async Task Email_is_verified_only_when_the_provider_says_so(string? claim, object? value, bool expected)
    {
        var h = new SignInHarness();
        var result = await h.SignInWithClaimsAsync(c =>
        {
            c.Remove("email_verified");
            if (claim != null) c[claim] = value!;
        });
        result.Identity!.EmailVerified.Should().Be(expected);
    }

    // ── the state ──

    [Fact]
    public async Task A_state_from_another_browser_is_refused_and_burned()
    {
        var h = new SignInHarness();
        var (state, nonce, _) = await h.BeginAsync();
        h.Provider.NextIdToken = h.Provider.Sign(h.Claims(nonce));

        (await h.SignIn.CompleteAsync(new OpenIdCallback(state, "good"), "attackers-browser"))
            .Status.Should().Be(OpenIdSignInStatus.InvalidState);
        (await h.SignIn.CompleteAsync(new OpenIdCallback(state, "good"), SignInHarness.Binding))
            .Status.Should().Be(OpenIdSignInStatus.InvalidState, "a state is removed by its first presentation");
        h.Provider.TokenRequests.Should().BeEmpty("no code is exchanged on a refused state");
    }

    [Fact]
    public async Task A_reused_state_is_refused()
    {
        var h = new SignInHarness();
        var (state, nonce, _) = await h.BeginAsync();
        h.Provider.NextIdToken = h.Provider.Sign(h.Claims(nonce));

        (await h.SignIn.CompleteAsync(new OpenIdCallback(state, "good"), SignInHarness.Binding)).Succeeded.Should().BeTrue();
        (await h.SignIn.CompleteAsync(new OpenIdCallback(state, "good"), SignInHarness.Binding))
            .Status.Should().Be(OpenIdSignInStatus.InvalidState);
    }

    [Fact]
    public async Task An_unknown_or_missing_state_or_binding_is_refused()
    {
        var h = new SignInHarness();
        var (state, _, _) = await h.BeginAsync();

        (await h.SignIn.CompleteAsync(new OpenIdCallback("made-up", "good"), SignInHarness.Binding)).Status.Should().Be(OpenIdSignInStatus.InvalidState);
        (await h.SignIn.CompleteAsync(new OpenIdCallback(null, "good"), SignInHarness.Binding)).Status.Should().Be(OpenIdSignInStatus.InvalidState);
        (await h.SignIn.CompleteAsync(new OpenIdCallback(state, "good"), "")).Status.Should().Be(OpenIdSignInStatus.InvalidState);
        h.States.Count.Should().Be(1, "a callback without a binding leaves the state for the browser that holds it");
    }

    [Fact]
    public async Task An_expired_state_is_refused()
    {
        var h = new SignInHarness();
        var (state, _, _) = await h.BeginAsync();
        h.Clock.Now += TimeSpan.FromMinutes(11);

        (await h.SignIn.CompleteAsync(new OpenIdCallback(state, "good"), SignInHarness.Binding)).Status.Should().Be(OpenIdSignInStatus.InvalidState);
    }

    [Fact]
    public async Task Begin_requires_a_binding()
    {
        var h = new SignInHarness();
        await FluentActions.Awaiting(() => h.SignIn.BeginAsync("")).Should().ThrowAsync<ArgumentException>();
    }

    // ── the provider ──

    [Fact]
    public async Task The_providers_error_callback_is_ProviderRefused_and_burns_the_state()
    {
        var h = new SignInHarness();
        var (state, _, _) = await h.BeginAsync(data: "/back");

        var result = await h.SignIn.CompleteAsync(new OpenIdCallback(state, null, "access_denied", "The user cancelled."), SignInHarness.Binding);

        result.Should().Be(new OpenIdSignInResult(OpenIdSignInStatus.ProviderRefused, null, "/back", "access_denied", "The user cancelled."));
        (await h.SignIn.CompleteAsync(new OpenIdCallback(state, "good"), SignInHarness.Binding)).Status.Should().Be(OpenIdSignInStatus.InvalidState);
        h.Provider.TokenRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task A_code_the_provider_refuses_is_CodeRejected()
    {
        var h = new SignInHarness();
        var (state, _, _) = await h.BeginAsync();

        var result = await h.SignIn.CompleteAsync(new OpenIdCallback(state, "stale"), SignInHarness.Binding);

        result.Status.Should().Be(OpenIdSignInStatus.CodeRejected);
        result.Error.Should().Be("invalid_grant");
        result.ErrorDescription.Should().Be("Bad code.");
    }

    [Fact]
    public async Task A_callback_without_code_is_CodeRejected()
    {
        var h = new SignInHarness();
        var (state, _, _) = await h.BeginAsync();
        (await h.SignIn.CompleteAsync(new OpenIdCallback(state, null), SignInHarness.Binding)).Status.Should().Be(OpenIdSignInStatus.CodeRejected);
    }

    [Fact]
    public async Task A_token_endpoint_server_error_is_ProviderUnavailable()
    {
        var h = new SignInHarness();
        var (state, _, _) = await h.BeginAsync();
        h.Provider.TokenStatusOverride = HttpStatusCode.ServiceUnavailable;

        (await h.SignIn.CompleteAsync(new OpenIdCallback(state, "good"), SignInHarness.Binding))
            .Status.Should().Be(OpenIdSignInStatus.ProviderUnavailable);
    }

    [Fact]
    public async Task Begin_throws_when_the_discovery_document_cannot_be_read()
    {
        var h = new SignInHarness();
        h.Provider.DiscoveryStatus = HttpStatusCode.InternalServerError;

        await FluentActions.Awaiting(() => h.SignIn.BeginAsync(SignInHarness.Binding))
            .Should().ThrowAsync<OpenIdProviderUnavailableException>();
    }
}
