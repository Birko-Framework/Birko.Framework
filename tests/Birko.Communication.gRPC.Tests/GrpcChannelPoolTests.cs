using System;
using Birko.Communication.gRPC;
using FluentAssertions;
using Xunit;

namespace Birko.Communication.gRPC.Tests;

[Collection("ChannelPool")]
public class GrpcChannelPoolTests : IDisposable
{
    public GrpcChannelPoolTests() => GrpcChannelPool.Clear();

    public void Dispose() => GrpcChannelPool.Clear();

    [Fact]
    public void GetChannel_Caches_Per_Endpoint()
    {
        var settings = new GrpcSettings { Endpoint = "https://localhost:5001" };

        var first = GrpcChannelPool.GetChannel(settings);
        var second = GrpcChannelPool.GetChannel(settings);

        first.Should().BeSameAs(second);
    }

    [Fact]
    public void GetChannel_Different_Endpoints_Are_Distinct()
    {
        var a = GrpcChannelPool.GetChannel(new GrpcSettings { Endpoint = "https://localhost:5001" });
        var b = GrpcChannelPool.GetChannel(new GrpcSettings { Endpoint = "https://localhost:5002" });

        a.Should().NotBeSameAs(b);
    }

    [Fact]
    public void GetChannel_Throws_When_Endpoint_Missing()
    {
        var act = () => GrpcChannelPool.GetChannel(new GrpcSettings());

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void GetChannel_Throws_When_Settings_Null()
    {
        var act = () => GrpcChannelPool.GetChannel(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Remove_Evicts_Channel()
    {
        var settings = new GrpcSettings { Endpoint = "https://localhost:5003" };
        GrpcChannelPool.GetChannel(settings);

        GrpcChannelPool.Remove("https://localhost:5003").Should().BeTrue();
        GrpcChannelPool.Remove("https://localhost:5003").Should().BeFalse();
    }

    [Fact]
    public void Remove_After_Eviction_Creates_New_Instance()
    {
        var settings = new GrpcSettings { Endpoint = "https://localhost:5004" };

        var first = GrpcChannelPool.GetChannel(settings);
        GrpcChannelPool.Remove("https://localhost:5004");
        var second = GrpcChannelPool.GetChannel(settings);

        first.Should().NotBeSameAs(second);
    }
}
