using System.Linq;
using Birko.Communication.gRPC.Server;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Birko.Communication.gRPC.Server.Tests;

public class GrpcServiceExtensionsTests
{
    [Fact]
    public void AddBirkoGrpc_Returns_Builder_And_Registers_Services()
    {
        var services = new ServiceCollection();

        var before = services.Count;
        var builder = services.AddBirkoGrpc();

        builder.Should().NotBeNull();
        services.Count.Should().BeGreaterThan(before);
    }

    [Fact]
    public void AddBirkoGrpc_With_Settings_Does_Not_Throw()
    {
        var services = new ServiceCollection();
        var settings = new GrpcServerSettings
        {
            EnableDetailedErrors = true,
            MaxReceiveMessageSizeBytes = 8 * 1024 * 1024,
        };

        var act = () => services.AddBirkoGrpc(settings);

        act.Should().NotThrow();
    }

    [Fact]
    public void AddBirkoGrpc_Throws_On_Null_Services()
    {
        IServiceCollection services = null!;

        var act = () => services.AddBirkoGrpc();

        act.Should().Throw<System.ArgumentNullException>();
    }
}
