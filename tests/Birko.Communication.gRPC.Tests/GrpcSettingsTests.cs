using Birko.Communication.gRPC;
using FluentAssertions;
using Xunit;

namespace Birko.Communication.gRPC.Tests;

public class GrpcSettingsTests
{
    [Fact]
    public void Endpoint_Aliases_Location()
    {
        var settings = new GrpcSettings { Endpoint = "https://api.example.com:443" };

        settings.Location.Should().Be("https://api.example.com:443");
        settings.Endpoint.Should().Be("https://api.example.com:443");
    }

    [Fact]
    public void Endpoint_Defaults_To_Empty_When_Location_Null()
    {
        var settings = new GrpcSettings();

        settings.Endpoint.Should().BeEmpty();
    }

    [Fact]
    public void Defaults_Are_Conservative()
    {
        var settings = new GrpcSettings();

        settings.MaxReceiveMessageSizeBytes.Should().BeNull();
        settings.MaxSendMessageSizeBytes.Should().BeNull();
        settings.DeadlineSeconds.Should().BeNull();
        settings.Credentials.Should().BeNull();
        settings.ExtraMetadata.Should().BeEmpty();
    }

    [Fact]
    public void ExtraMetadata_Is_Mutable()
    {
        var settings = new GrpcSettings();
        settings.ExtraMetadata["x-tenant"] = "acme";

        settings.ExtraMetadata.Should().ContainKey("x-tenant").WhoseValue.Should().Be("acme");
    }
}
