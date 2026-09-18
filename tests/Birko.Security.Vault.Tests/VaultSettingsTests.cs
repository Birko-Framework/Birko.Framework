using Birko.Security.Vault;
using FluentAssertions;
using Xunit;

namespace Birko.Security.Vault.Tests;

public class VaultSettingsTests
{
    [Fact]
    public void DefaultSettings_HasCorrectDefaults()
    {
        var settings = new VaultSettings();

        settings.Address.Should().Be("http://127.0.0.1:8200");
        settings.MountPath.Should().Be("secret");
        settings.KvVersion.Should().Be(2);
        settings.TimeoutSeconds.Should().Be(30);
        settings.Namespace.Should().BeNull();
        settings.Token.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithParameters_SetsProperties()
    {
        var settings = new VaultSettings("https://vault.example.com:8200", "hvs.token123", "kv");

        settings.Address.Should().Be("https://vault.example.com:8200");
        settings.Token.Should().Be("hvs.token123");
        settings.MountPath.Should().Be("kv");
    }

    [Fact]
    public void Address_MapsToLocation()
    {
        var settings = new VaultSettings { Address = "https://vault.local" };
        settings.Location.Should().Be("https://vault.local");
    }

    [Fact]
    public void Token_MapsToPassword()
    {
        var settings = new VaultSettings { Token = "my-token" };
        settings.Password.Should().Be("my-token");
    }

    [Fact]
    public void Token_SetNull_StoresEmptyPassword_ReadsBackNull()
    {
        // CR-L353: a null Token must not be forced into the non-nullable Password via `value!`; it normalizes
        // to string.Empty and reads back as null (no token configured).
        var settings = new VaultSettings { Token = "prior" };
        settings.Token = null;

        settings.Password.Should().Be(string.Empty);
        settings.Token.Should().BeNull();
    }

    [Fact]
    public void Token_SetEmpty_ReadsBackNull()
    {
        var settings = new VaultSettings { Token = string.Empty };
        settings.Token.Should().BeNull();
        settings.Password.Should().Be(string.Empty);
    }

    [Fact]
    public void MountPath_MapsToName()
    {
        var settings = new VaultSettings { MountPath = "kv-v2" };
        settings.Name.Should().Be("kv-v2");
    }

    [Fact]
    public void GetId_ReturnsLocationAndName()
    {
        var settings = new VaultSettings("https://vault.example.com", "token", "secret");
        settings.GetId().Should().Contain("vault.example.com");
        settings.GetId().Should().Contain("secret");
    }

    [Fact]
    public void ExtendsPasswordSettings()
    {
        var settings = new VaultSettings();
        settings.Should().BeAssignableTo<Birko.Configuration.PasswordSettings>();
        settings.Should().BeAssignableTo<Birko.Configuration.ISettings>();
    }
}
