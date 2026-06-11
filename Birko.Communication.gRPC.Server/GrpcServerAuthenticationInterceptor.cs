using System;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Birko.Communication.gRPC.Server;

/// <summary>
/// Server-side interceptor scaffold that validates request metadata before any handler runs.
/// Supply a validator that inspects the incoming <see cref="Metadata"/> (e.g. an authorization
/// header) and returns whether the call is authenticated. On failure an
/// <see cref="RpcException"/> with <see cref="StatusCode.Unauthenticated"/> is thrown.
/// </summary>
public class GrpcServerAuthenticationInterceptor : Interceptor
{
    private readonly Func<Metadata, ServerCallContext, Task<bool>> _validate;
    private readonly string _failureMessage;

    /// <param name="validate">Returns true when the request headers represent an authenticated caller.</param>
    /// <param name="failureMessage">Status detail used when validation fails.</param>
    public GrpcServerAuthenticationInterceptor(
        Func<Metadata, ServerCallContext, Task<bool>> validate,
        string failureMessage = "Authentication failed")
    {
        _validate = validate ?? throw new ArgumentNullException(nameof(validate));
        _failureMessage = failureMessage;
    }

    private async Task EnsureAuthenticatedAsync(ServerCallContext context)
    {
        var ok = await _validate(context.RequestHeaders, context).ConfigureAwait(false);
        if (!ok)
            throw new RpcException(new Status(StatusCode.Unauthenticated, _failureMessage));
    }

    /// <inheritdoc />
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        await EnsureAuthenticatedAsync(context).ConfigureAwait(false);
        return await continuation(request, context).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        ServerCallContext context,
        ClientStreamingServerMethod<TRequest, TResponse> continuation)
    {
        await EnsureAuthenticatedAsync(context).ConfigureAwait(false);
        return await continuation(requestStream, context).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        await EnsureAuthenticatedAsync(context).ConfigureAwait(false);
        await continuation(request, responseStream, context).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async Task DuplexStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        DuplexStreamingServerMethod<TRequest, TResponse> continuation)
    {
        await EnsureAuthenticatedAsync(context).ConfigureAwait(false);
        await continuation(requestStream, responseStream, context).ConfigureAwait(false);
    }
}
