using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.Communication.gRPC.Server;
using FluentAssertions;
using Grpc.Core;
using Xunit;

namespace Birko.Communication.gRPC.Server.Tests;

public class GrpcServerAuthenticationInterceptorTests
{
    /// <summary>Minimal in-memory <see cref="ServerCallContext"/> exposing the request headers.</summary>
    private sealed class FakeServerCallContext : ServerCallContext
    {
        private readonly Metadata _requestHeaders;
        public FakeServerCallContext(Metadata requestHeaders) => _requestHeaders = requestHeaders;

        protected override string MethodCore => "test.Service/Echo";
        protected override string HostCore => string.Empty;
        protected override string PeerCore => "127.0.0.1";
        protected override DateTime DeadlineCore => DateTime.UtcNow.AddMinutes(1);
        protected override Metadata RequestHeadersCore => _requestHeaders;
        protected override CancellationToken CancellationTokenCore => CancellationToken.None;
        protected override Metadata ResponseTrailersCore { get; } = new();
        protected override Status StatusCore { get; set; }
        protected override WriteOptions? WriteOptionsCore { get; set; }
        protected override AuthContext AuthContextCore => new(null, new());
        protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options)
            => throw new NotSupportedException();
        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
    }

    private static ServerCallContext CreateContext(Metadata headers) => new FakeServerCallContext(headers);

    [Fact]
    public async Task UnaryServerHandler_Invokes_Continuation_When_Authenticated()
    {
        var interceptor = new GrpcServerAuthenticationInterceptor((headers, ctx) =>
            Task.FromResult(headers.GetValue("authorization") == "Bearer good"));

        var headers = new Metadata { { "authorization", "Bearer good" } };
        var context = CreateContext(headers);

        var called = false;
        Task<string> Continuation(string req, ServerCallContext ctx)
        {
            called = true;
            return Task.FromResult("resp:" + req);
        }

        var result = await interceptor.UnaryServerHandler("ping", context, Continuation);

        called.Should().BeTrue();
        result.Should().Be("resp:ping");
    }

    [Fact]
    public async Task UnaryServerHandler_Throws_Unauthenticated_When_Validation_Fails()
    {
        var interceptor = new GrpcServerAuthenticationInterceptor(
            (headers, ctx) => Task.FromResult(false), "nope");

        var context = CreateContext(new Metadata());

        var called = false;
        Task<string> Continuation(string req, ServerCallContext ctx)
        {
            called = true;
            return Task.FromResult("resp");
        }

        var act = async () => await interceptor.UnaryServerHandler("ping", context, Continuation);

        var ex = (await act.Should().ThrowAsync<RpcException>()).Which;
        ex.StatusCode.Should().Be(StatusCode.Unauthenticated);
        ex.Status.Detail.Should().Be("nope");
        called.Should().BeFalse();
    }

    [Fact]
    public void Constructor_Rejects_Null_Validator()
    {
        var act = () => new GrpcServerAuthenticationInterceptor(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
