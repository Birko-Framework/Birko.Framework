using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Birko.Communication.gRPC;
using FluentAssertions;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Xunit;

namespace Birko.Communication.gRPC.Tests;

public class GrpcAuthenticationInterceptorTests
{
    private static readonly Marshaller<string> StringMarshaller =
        Marshallers.Create(s => Encoding.UTF8.GetBytes(s), b => Encoding.UTF8.GetString(b));

    private static readonly Method<string, string> UnaryMethod = new(
        MethodType.Unary, "test.Service", "Echo", StringMarshaller, StringMarshaller);

    private static AsyncUnaryCall<string> FakeCall(string response) => new(
        Task.FromResult(response),
        Task.FromResult(new Metadata()),
        () => Status.DefaultSuccess,
        () => new Metadata(),
        () => { });

    [Fact]
    public void AsyncUnaryCall_Injects_Bearer_Token()
    {
        var interceptor = new GrpcAuthenticationInterceptor(() => "test-token");
        var context = new ClientInterceptorContext<string, string>(UnaryMethod, null, new CallOptions());

        Metadata? captured = null;
        AsyncUnaryCall<string> Continuation(string req, ClientInterceptorContext<string, string> ctx)
        {
            captured = ctx.Options.Headers;
            return FakeCall("ok");
        }

        interceptor.AsyncUnaryCall("req", context, Continuation);

        captured.Should().NotBeNull();
        captured!.Get("authorization")!.Value.Should().Be("Bearer test-token");
    }

    [Fact]
    public void AsyncUnaryCall_Omits_Header_When_Token_Empty()
    {
        var interceptor = new GrpcAuthenticationInterceptor(() => null);
        var context = new ClientInterceptorContext<string, string>(UnaryMethod, null, new CallOptions());

        Metadata? captured = null;
        AsyncUnaryCall<string> Continuation(string req, ClientInterceptorContext<string, string> ctx)
        {
            captured = ctx.Options.Headers;
            return FakeCall("ok");
        }

        interceptor.AsyncUnaryCall("req", context, Continuation);

        captured.Should().NotBeNull();
        captured!.Get("authorization").Should().BeNull();
    }

    [Fact]
    public void AsyncUnaryCall_Adds_Extra_Metadata_And_Custom_Scheme()
    {
        var extra = new Dictionary<string, string> { ["x-tenant"] = "acme" };
        var interceptor = new GrpcAuthenticationInterceptor(
            () => "abc", headerName: "x-api-key", scheme: "", extraMetadata: extra);
        var context = new ClientInterceptorContext<string, string>(UnaryMethod, null, new CallOptions());

        Metadata? captured = null;
        AsyncUnaryCall<string> Continuation(string req, ClientInterceptorContext<string, string> ctx)
        {
            captured = ctx.Options.Headers;
            return FakeCall("ok");
        }

        interceptor.AsyncUnaryCall("req", context, Continuation);

        captured!.Get("x-api-key")!.Value.Should().Be("abc");
        captured!.Get("x-tenant")!.Value.Should().Be("acme");
    }

    [Fact]
    public void BlockingUnaryCall_Injects_Token()
    {
        var interceptor = new GrpcAuthenticationInterceptor(() => "tok");
        var context = new ClientInterceptorContext<string, string>(UnaryMethod, null, new CallOptions());

        Metadata? captured = null;
        string Continuation(string req, ClientInterceptorContext<string, string> ctx)
        {
            captured = ctx.Options.Headers;
            return "resp";
        }

        var result = interceptor.BlockingUnaryCall("req", context, Continuation);

        result.Should().Be("resp");
        captured!.Get("authorization")!.Value.Should().Be("Bearer tok");
    }

    [Fact]
    public void Custom_Mutator_Constructor_Populates_Headers()
    {
        var interceptor = new GrpcAuthenticationInterceptor(md => md.Add("x-trace", "123"));
        var context = new ClientInterceptorContext<string, string>(UnaryMethod, null, new CallOptions());

        Metadata? captured = null;
        AsyncUnaryCall<string> Continuation(string req, ClientInterceptorContext<string, string> ctx)
        {
            captured = ctx.Options.Headers;
            return FakeCall("ok");
        }

        interceptor.AsyncUnaryCall("req", context, Continuation);

        captured!.Get("x-trace")!.Value.Should().Be("123");
    }

    [Fact]
    public void Token_Provider_Constructor_Rejects_Null()
    {
        var act = () => new GrpcAuthenticationInterceptor((Func<string?>)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ReusedCallOptions_DoesNotAccumulateDuplicateHeaders_OrMutateCaller()
    {
        // CR-M047: WithAuth used to mutate the caller-supplied Metadata in place and Add() (not
        // replace) the auth header, so reusing a CallOptions across calls accumulated duplicate
        // 'authorization' entries. It must operate on a copy.
        var interceptor = new GrpcAuthenticationInterceptor(() => "tok");
        var callerHeaders = new Metadata();
        var context = new ClientInterceptorContext<string, string>(
            UnaryMethod, null, new CallOptions(headers: callerHeaders));

        Metadata? captured = null;
        AsyncUnaryCall<string> Continuation(string req, ClientInterceptorContext<string, string> ctx)
        {
            captured = ctx.Options.Headers;
            return FakeCall("ok");
        }

        interceptor.AsyncUnaryCall("req", context, Continuation);
        var first = captured;
        interceptor.AsyncUnaryCall("req", context, Continuation);
        var second = captured;

        callerHeaders.GetAll("authorization").Should().BeEmpty("the caller's Metadata must not be mutated");
        first!.GetAll("authorization").Should().HaveCount(1);
        second!.GetAll("authorization").Should().HaveCount(1, "each call injects exactly one header, no accumulation");
        first.Should().NotBeSameAs(callerHeaders);
    }
}
