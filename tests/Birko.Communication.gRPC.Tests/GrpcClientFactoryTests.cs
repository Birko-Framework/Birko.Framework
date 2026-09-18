using System;
using System.Text;
using System.Threading.Tasks;
using Birko.Communication.gRPC;
using FluentAssertions;
using Grpc.Core;
using Xunit;

namespace Birko.Communication.gRPC.Tests;

// TASK-459: GrpcChannelPool._channels is STATIC and GrpcChannelPoolTests disposes every pooled
// channel in its cleanup. That class names the collection; this one did not — and xUnit serialises
// classes WITHIN a collection while running different collections in PARALLEL, so the disposal was
// free to land in the middle of CreateClient_From_Settings_Uses_Pooled_Channel, which then failed
// with ObjectDisposedException on a GrpcChannel it was legitimately holding. Failed 1 of 5 CI runs
// and never locally, because the two classes have to genuinely overlap.
// Both classes must name the collection, or neither is serialised against the other.
[Collection("ChannelPool")]
public class GrpcClientFactoryTests
{
    private static readonly Marshaller<string> StringMarshaller =
        Marshallers.Create(s => Encoding.UTF8.GetBytes(s), b => Encoding.UTF8.GetString(b));

    /// <summary>A minimal generated-style client: ClientBase with a CallInvoker constructor.</summary>
    private sealed class FakeClient : ClientBase
    {
        public FakeClient(CallInvoker callInvoker) : base(callInvoker) { }

        public CallInvoker Invoker => CallInvoker;
    }

    private sealed class RecordingCallInvoker : CallInvoker
    {
        public override TResponse BlockingUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
            => throw new NotImplementedException();
        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
            => throw new NotImplementedException();
        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
            => throw new NotImplementedException();
        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options)
            => throw new NotImplementedException();
        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options)
            => throw new NotImplementedException();
    }

    [Fact]
    public void CreateClient_From_Invoker_Builds_Client()
    {
        var invoker = new RecordingCallInvoker();

        var client = GrpcClientFactory.CreateClient<FakeClient>(invoker);

        client.Should().NotBeNull();
    }

    [Fact]
    public void CreateClient_With_Interceptors_Wraps_Invoker()
    {
        var invoker = new RecordingCallInvoker();
        var interceptor = new GrpcAuthenticationInterceptor(() => "tok");

        var client = GrpcClientFactory.CreateClient<FakeClient>(invoker, interceptor);

        // Intercept() returns a wrapping invoker, not the original.
        client.Invoker.Should().NotBeSameAs(invoker);
    }

    [Fact]
    public void CreateClient_From_Settings_Uses_Pooled_Channel()
    {
        GrpcChannelPool.Clear();
        var settings = new GrpcSettings { Endpoint = "https://localhost:5010" };

        var client = GrpcClientFactory.CreateClient<FakeClient>(settings);

        client.Should().NotBeNull();
        GrpcChannelPool.Clear();
    }

    [Fact]
    public void CreateClient_Throws_On_Null_Invoker()
    {
        var act = () => GrpcClientFactory.CreateClient<FakeClient>((CallInvoker)null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
