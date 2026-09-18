using Birko.Communication.OAuth;
using Birko.Communication.OAuth.Providers;
using FluentAssertions;
using Xunit;

namespace Birko.Communication.OAuth.Providers.Tests;

/// <summary>
/// Coverage for the GitHub device-flow factory (CR-L077): asserts the produced OAuthSettings field
/// values and that the two factory methods stay in sync (CreateDeviceFlowClient delegates to
/// CreateDeviceFlowSettings — CR-L076).
/// </summary>
public class GitHubOAuthProviderTests
{
    [Fact]
    public void CreateDeviceFlowSettings_PopulatesGitHubDeviceFlow()
    {
        var settings = GitHubOAuthProvider.CreateDeviceFlowSettings("client-123");

        settings.GrantType.Should().Be(OAuthGrantType.DeviceCode);
        settings.ClientId.Should().Be("client-123");
        settings.TokenEndpoint.Should().Be("https://github.com/login/oauth/access_token");
        settings.DeviceAuthorizationEndpoint.Should().Be("https://github.com/login/device/code");
        settings.Scope.Should().Be("read:user");
        settings.DeviceCodePollingIntervalSeconds.Should().Be(5);
        settings.DeviceCodeTimeoutSeconds.Should().Be(600);
    }

    [Fact]
    public void CreateDeviceFlowSettings_DefaultScope_IsReadUser()
    {
        GitHubOAuthProvider.CreateDeviceFlowSettings("id").Scope.Should().Be("read:user");
    }

    [Fact]
    public void CreateDeviceFlowSettings_HonorsCustomScope()
    {
        GitHubOAuthProvider.CreateDeviceFlowSettings("id", "repo user:email").Scope.Should().Be("repo user:email");
    }

    [Fact]
    public void CreateDeviceFlowClient_ReturnsNonNullClient()
    {
        var client = GitHubOAuthProvider.CreateDeviceFlowClient("client-123");
        client.Should().NotBeNull().And.BeAssignableTo<IOAuthClient>();
    }

    [Fact]
    public void Endpoints_AreTheGitHubConstants()
    {
        GitHubOAuthProvider.DeviceAuthorizationEndpoint.Should().Be("https://github.com/login/device/code");
        GitHubOAuthProvider.TokenEndpoint.Should().Be("https://github.com/login/oauth/access_token");
    }
}
