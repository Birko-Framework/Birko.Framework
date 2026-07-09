using System;
using System.Collections.Generic;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Birko.Communication.gRPC;

/// <summary>
/// Client-side interceptor scaffold that injects authentication metadata (and optional static
/// metadata) into every outgoing call. Supply a token provider for a bearer-style header, or a
/// custom <see cref="Action{Metadata}"/> to populate headers arbitrarily.
/// </summary>
public class GrpcAuthenticationInterceptor : Interceptor
{
    private readonly Action<Metadata> _mutate;

    /// <summary>
    /// Creates an interceptor that adds a single header produced by <paramref name="tokenProvider"/>.
    /// When the provider returns null/empty, no header is added (anonymous call).
    /// </summary>
    /// <param name="tokenProvider">Returns the current token (e.g. an access token), or null.</param>
    /// <param name="headerName">Metadata key to set. Defaults to "authorization".</param>
    /// <param name="scheme">Scheme prefix prepended to the token. Defaults to "Bearer". Pass "" for none.</param>
    /// <param name="extraMetadata">Optional static metadata added to every call.</param>
    public GrpcAuthenticationInterceptor(
        Func<string?> tokenProvider,
        string headerName = "authorization",
        string scheme = "Bearer",
        IReadOnlyDictionary<string, string>? extraMetadata = null)
    {
        if (tokenProvider == null)
            throw new ArgumentNullException(nameof(tokenProvider));

        _mutate = metadata =>
        {
            if (extraMetadata != null)
            {
                foreach (var kvp in extraMetadata)
                    metadata.Add(kvp.Key, kvp.Value);
            }

            var token = tokenProvider();
            if (!string.IsNullOrEmpty(token))
            {
                var value = string.IsNullOrEmpty(scheme) ? token : $"{scheme} {token}";
                metadata.Add(headerName, value);
            }
        };
    }

    /// <summary>
    /// Creates an interceptor that lets <paramref name="mutateMetadata"/> populate the call headers directly.
    /// </summary>
    public GrpcAuthenticationInterceptor(Action<Metadata> mutateMetadata)
    {
        _mutate = mutateMetadata ?? throw new ArgumentNullException(nameof(mutateMetadata));
    }

    private ClientInterceptorContext<TRequest, TResponse> WithAuth<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context)
        where TRequest : class
        where TResponse : class
    {
        // Operate on a COPY of the inbound headers, never the caller's Metadata instance. CallOptions
        // /Metadata can be reused across calls, and _mutate Add()s (does not replace) the auth header,
        // so mutating the original accumulates duplicate authorization/x-tenant entries on every reuse
        // (CR-M047).
        var headers = new Metadata();
        if (context.Options.Headers != null)
        {
            foreach (var entry in context.Options.Headers)
                headers.Add(entry);
        }
        _mutate(headers);
        var options = context.Options.WithHeaders(headers);
        return new ClientInterceptorContext<TRequest, TResponse>(context.Method, context.Host, options);
    }

    /// <inheritdoc />
    public override TResponse BlockingUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        BlockingUnaryCallContinuation<TRequest, TResponse> continuation)
        => continuation(request, WithAuth(context));

    /// <inheritdoc />
    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
        => continuation(request, WithAuth(context));

    /// <inheritdoc />
    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation)
        => continuation(request, WithAuth(context));

    /// <inheritdoc />
    public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncClientStreamingCallContinuation<TRequest, TResponse> continuation)
        => continuation(WithAuth(context));

    /// <inheritdoc />
    public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncDuplexStreamingCallContinuation<TRequest, TResponse> continuation)
        => continuation(WithAuth(context));
}
