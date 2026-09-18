using Birko.Communication.gRPC.Server;
using FluentAssertions;
using Xunit;

namespace Birko.Communication.gRPC.Server.Tests;

public class GrpcServerSettingsTests
{
    [Fact]
    public void Defaults_Are_Conservative()
    {
        var settings = new GrpcServerSettings();

        settings.EnableDetailedErrors.Should().BeFalse();
        settings.MaxReceiveMessageSizeBytes.Should().BeNull();
        settings.MaxSendMessageSizeBytes.Should().BeNull();
        settings.EnableReflection.Should().BeFalse();
    }

    [Fact]
    public void Properties_Round_Trip()
    {
        var settings = new GrpcServerSettings
        {
            EnableDetailedErrors = true,
            MaxReceiveMessageSizeBytes = 1024,
            MaxSendMessageSizeBytes = 2048,
            EnableReflection = true,
        };

        settings.EnableDetailedErrors.Should().BeTrue();
        settings.MaxReceiveMessageSizeBytes.Should().Be(1024);
        settings.MaxSendMessageSizeBytes.Should().Be(2048);
        settings.EnableReflection.Should().BeTrue();
    }
}
