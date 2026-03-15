using Birko.Security.AzureKeyVault;
using FluentAssertions;
using Xunit;

namespace Birko.Security.AzureKeyVault.Tests;

public class AzureKeyVaultSettingsTests
{
    [Fact]
    public void DefaultSettings_HasEmptyVaultUri()
    {
        var settings = new AzureKeyVaultSettings();
        settings.VaultUri.Should().BeEmpty();
        settings.TimeoutSeconds.Should().Be(30);
    }

    [Fact]
    public void Constructor_WithParameters_SetsProperties()
    {
        var settings = new AzureKeyVaultSettings("https://myvault.vault.azure.net/", "tenant-123", "client-456", "secret-789");

        settings.VaultUri.Should().Be("https://myvault.vault.azure.net/");
        settings.TenantId.Should().Be("tenant-123");
        settings.ClientId.Should().Be("client-456");
        settings.ClientSecret.Should().Be("secret-789");
    }

    [Fact]
    public void VaultUri_MapsToLocation()
    {
        var settings = new AzureKeyVaultSettings { VaultUri = "https://test.vault.azure.net/" };
        settings.Location.Should().Be("https://test.vault.azure.net/");
    }

    [Fact]
    public void TenantId_MapsToName()
    {
        var settings = new AzureKeyVaultSettings { TenantId = "my-tenant" };
        settings.Name.Should().Be("my-tenant");
    }

    [Fact]
    public void ClientId_MapsToUserName()
    {
        var settings = new AzureKeyVaultSettings { ClientId = "app-id" };
        settings.UserName.Should().Be("app-id");
    }

    [Fact]
    public void ClientSecret_MapsToPassword()
    {
        var settings = new AzureKeyVaultSettings { ClientSecret = "s3cret" };
        settings.Password.Should().Be("s3cret");
    }

    [Fact]
    public void ExtendsRemoteSettings()
    {
        var settings = new AzureKeyVaultSettings();
        settings.Should().BeAssignableTo<Birko.Data.Stores.RemoteSettings>();
        settings.Should().BeAssignableTo<Birko.Data.Stores.ISettings>();
    }
}
