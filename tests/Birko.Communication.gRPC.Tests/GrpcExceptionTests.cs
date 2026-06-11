using System;
using Birko.Communication.gRPC;
using FluentAssertions;
using Grpc.Core;
using Xunit;

namespace Birko.Communication.gRPC.Tests;

public class GrpcExceptionTests
{
    [Fact]
    public void FromRpcException_Maps_Status_And_Detail()
    {
        var rpc = new RpcException(new Status(StatusCode.NotFound, "missing"));

        var ex = GrpcException.FromRpcException(rpc);

        ex.StatusCode.Should().Be(StatusCode.NotFound);
        ex.Detail.Should().Be("missing");
        ex.InnerException.Should().BeSameAs(rpc);
        ex.Message.Should().Contain("NotFound").And.Contain("missing");
    }

    [Fact]
    public void FromRpcException_Carries_Trailers()
    {
        var trailers = new Metadata { { "x-detail", "boom" } };
        var rpc = new RpcException(new Status(StatusCode.Internal, "err"), trailers);

        var ex = GrpcException.FromRpcException(rpc);

        ex.Trailers.Should().NotBeNull();
        ex.Trailers!.Get("x-detail")!.Value.Should().Be("boom");
    }

    [Fact]
    public void FromRpcException_Throws_On_Null()
    {
        var act = () => GrpcException.FromRpcException(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_Sets_Properties()
    {
        var ex = new GrpcException(StatusCode.Unauthenticated, "no token");

        ex.StatusCode.Should().Be(StatusCode.Unauthenticated);
        ex.Detail.Should().Be("no token");
    }
}
