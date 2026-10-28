using System.Net;
using FluentAssertions;
using Xunit;

namespace Birko.Communication.OAuth.OpenIdConnect.Tests;

/// <summary>TASK-778: the discovery document and key set are cached on the clock; a key miss refreshes once, rate-limited.</summary>
public class SigningKeyRefreshTests
{
    [Fact]
    public async Task Metadata_is_fetched_once_and_reused()
    {
        var h = new SignInHarness();
        await h.SignInWithAsync(h.Provider.Sign);
        await h.SignInWithAsync(h.Provider.Sign);

        h.Provider.DiscoveryRequests.Should().Be(1);
        h.Provider.JwksRequests.Should().Be(1);
    }

    [Fact]
    public async Task A_rotated_key_is_picked_up_by_one_refresh()
    {
        var h = new SignInHarness();
        (await h.SignInWithAsync(h.Provider.Sign)).Succeeded.Should().BeTrue();

        h.Clock.Now += TimeSpan.FromMinutes(10);
        var rotated = FakeOpenIdProvider.NewKey("key-2");
        h.Provider.PublishedKeys.Add(rotated);
        h.Provider.SigningKey = rotated;

        var result = await h.SignInWithAsync(h.Provider.Sign);

        result.Status.Should().Be(OpenIdSignInStatus.Succeeded, result.Error);
        h.Provider.JwksRequests.Should().Be(2);
    }

    [Fact]
    public async Task Tokens_with_unknown_keys_refresh_at_most_once_per_interval()
    {
        var h = new SignInHarness();
        await h.SignInWithAsync(h.Provider.Sign);
        h.Clock.Now += TimeSpan.FromMinutes(10);
        var stranger = FakeOpenIdProvider.NewKey("unknown");

        for (var i = 0; i < 5; i++)
            (await h.SignInWithClaimsAsync(_ => { }, stranger)).Status.Should().Be(OpenIdSignInStatus.InvalidIdToken);

        h.Provider.JwksRequests.Should().Be(2, "one refresh for the first miss, none for the next four within five minutes");

        h.Clock.Now += TimeSpan.FromMinutes(6);
        await h.SignInWithClaimsAsync(_ => { }, stranger);
        h.Provider.JwksRequests.Should().Be(3);
    }

    [Fact]
    public async Task Metadata_is_fetched_again_after_the_refresh_interval()
    {
        var h = new SignInHarness();
        await h.SignInWithAsync(h.Provider.Sign);
        h.Clock.Now += TimeSpan.FromHours(25);
        await h.SignInWithAsync(h.Provider.Sign);

        h.Provider.DiscoveryRequests.Should().Be(2);
    }

    [Fact]
    public async Task A_provider_down_at_refresh_time_keeps_the_cached_keys()
    {
        var h = new SignInHarness();
        await h.SignInWithAsync(h.Provider.Sign);
        h.Clock.Now += TimeSpan.FromHours(25);
        h.Provider.DiscoveryStatus = HttpStatusCode.ServiceUnavailable;

        (await h.SignInWithAsync(h.Provider.Sign)).Status.Should().Be(OpenIdSignInStatus.Succeeded);
        (await h.SignInWithAsync(h.Provider.Sign)).Status.Should().Be(OpenIdSignInStatus.Succeeded);
        h.Provider.DiscoveryRequests.Should().Be(2, "a failed refresh is not retried on every request");
    }

    [Fact]
    public async Task With_nothing_cached_an_unreachable_provider_is_ProviderUnavailable()
    {
        var h = new SignInHarness();
        var validator = new IdTokenValidator(h.Options, new HttpClient(h.Provider), h.Clock);
        h.Provider.DiscoveryStatus = HttpStatusCode.BadGateway;

        var result = await validator.ValidateAsync(h.Provider.Sign(h.Claims("n")), "n");

        result.ProviderUnavailable.Should().BeTrue();
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task The_validator_works_on_its_own()
    {
        var h = new SignInHarness();
        var validator = new IdTokenValidator(h.Options, new HttpClient(h.Provider), h.Clock);

        var result = await validator.ValidateAsync(h.Provider.Sign(h.Claims("n-1")), "n-1");

        result.IsValid.Should().BeTrue(result.Error);
        result.Identity!.Subject.Should().Be("user-1");
    }
}
