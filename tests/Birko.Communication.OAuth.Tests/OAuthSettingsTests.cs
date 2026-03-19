using FluentAssertions;
using Xunit;

namespace Birko.Communication.OAuth.Tests;

public class OAuthSettingsTests
{
    [Fact]
    public void TokenEndpoint_MapsToLocation()
    {
        var settings = new OAuthSettings { TokenEndpoint = "https://auth.example.com/token" };
        settings.Location.Should().Be("https://auth.example.com/token");
    }

    [Fact]
    public void ClientId_MapsToUserName()
    {
        var settings = new OAuthSettings { ClientId = "my-client" };
        settings.UserName.Should().Be("my-client");
    }

    [Fact]
    public void ClientSecret_MapsToPassword()
    {
        var settings = new OAuthSettings { ClientSecret = "secret123" };
        settings.Password.Should().Be("secret123");
    }

    [Fact]
    public void Defaults_AreCorrect()
    {
        var settings = new OAuthSettings();
        settings.GrantType.Should().Be(OAuthGrantType.ClientCredentials);
        settings.TokenExpiryBufferSeconds.Should().Be(60);
        settings.TimeoutSeconds.Should().Be(30);
        settings.DeviceCodePollingIntervalSeconds.Should().Be(5);
        settings.DeviceCodeTimeoutSeconds.Should().Be(300);
    }

    [Fact]
    public void ExtraParameters_DefaultsToEmpty()
    {
        var settings = new OAuthSettings();
        settings.ExtraParameters.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void Location_ReturnsTokenEndpoint()
    {
        var settings = new OAuthSettings();
        settings.Location = "https://auth.example.com/token";
        settings.TokenEndpoint.Should().Be("https://auth.example.com/token");
    }

    [Fact]
    public void RemoteSettings_Inheritance_Works()
    {
        var settings = new OAuthSettings
        {
            TokenEndpoint = "https://auth.example.com/token",
            ClientId = "client1",
            ClientSecret = "secret",
            Port = 443,
            UseSecure = true
        };

        settings.Port.Should().Be(443);
        settings.UseSecure.Should().BeTrue();
    }
}
