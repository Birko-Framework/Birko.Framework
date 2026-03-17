using FluentAssertions;
using Xunit;

namespace Birko.Storage.AzureBlob.Tests.AzureBlob;

public class AzureBlobSettingsTests
{
    [Fact]
    public void Constructor_Default_SetsDefaults()
    {
        var settings = new AzureBlobSettings();

        settings.StorageAccountUri.Should().BeEmpty();
        settings.ContainerName.Should().BeEmpty();
        settings.TenantId.Should().BeNull();
        settings.ClientId.Should().BeNull();
        settings.ClientSecret.Should().BeNull();
        settings.PathPrefix.Should().BeNull();
        settings.TimeoutSeconds.Should().Be(30);
        settings.DefaultOptions.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithParameters_MapsPropertiesCorrectly()
    {
        var settings = new AzureBlobSettings(
            "https://myaccount.blob.core.windows.net",
            "my-container",
            "tenant-id",
            "client-id",
            "client-secret",
            "tenant-123/");

        settings.StorageAccountUri.Should().Be("https://myaccount.blob.core.windows.net");
        settings.ContainerName.Should().Be("my-container");
        settings.TenantId.Should().Be("tenant-id");
        settings.ClientId.Should().Be("client-id");
        settings.ClientSecret.Should().Be("client-secret");
        settings.PathPrefix.Should().Be("tenant-123/");
    }

    [Fact]
    public void StorageAccountUri_MapsToLocation()
    {
        var settings = new AzureBlobSettings { StorageAccountUri = "https://test.blob.core.windows.net" };

        settings.Location.Should().Be("https://test.blob.core.windows.net");
    }

    [Fact]
    public void TenantId_MapsToName()
    {
        var settings = new AzureBlobSettings { TenantId = "my-tenant" };

        settings.Name.Should().Be("my-tenant");
    }

    [Fact]
    public void ClientId_MapsToUserName()
    {
        var settings = new AzureBlobSettings { ClientId = "my-client" };

        settings.UserName.Should().Be("my-client");
    }

    [Fact]
    public void ClientSecret_MapsToPassword()
    {
        var settings = new AzureBlobSettings { ClientSecret = "my-secret" };

        settings.Password.Should().Be("my-secret");
    }

    [Fact]
    public void ContainerName_CanBeSetAndRead()
    {
        var settings = new AzureBlobSettings { ContainerName = "test-container" };

        settings.ContainerName.Should().Be("test-container");
    }

    [Fact]
    public void DefaultOptions_CanBeSetAndRead()
    {
        var options = new StorageOptions { OverwriteExisting = true, MaxFileSize = 1024 };
        var settings = new AzureBlobSettings { DefaultOptions = options };

        settings.DefaultOptions.Should().BeSameAs(options);
        settings.DefaultOptions!.MaxFileSize.Should().Be(1024);
    }
}
