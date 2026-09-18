using System;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Birko.Communication.gRPC;

/// <summary>
/// Creates strongly-typed generated gRPC clients over a pooled <see cref="Grpc.Net.Client.GrpcChannel"/>,
/// optionally wrapping the call pipeline with interceptors (e.g. <see cref="GrpcAuthenticationInterceptor"/>).
/// </summary>
public static class GrpcClientFactory
{
    /// <summary>
    /// Creates an instance of a generated client (a <see cref="ClientBase"/> subclass) bound to the
    /// channel for <paramref name="settings"/>, with the supplied interceptors applied in order.
    /// </summary>
    /// <typeparam name="TClient">A generated gRPC client type with a <see cref="CallInvoker"/> constructor.</typeparam>
    /// <param name="settings">Connection settings selecting the pooled channel.</param>
    /// <param name="interceptors">Optional interceptors applied to every call made through the client.</param>
    public static TClient CreateClient<TClient>(GrpcSettings settings, params Interceptor[] interceptors)
        where TClient : ClientBase
    {
        var channel = GrpcChannelPool.GetChannel(settings);
        return CreateClient<TClient>(channel.CreateCallInvoker(), interceptors);
    }

    /// <summary>
    /// Creates an instance of a generated client over an explicit <see cref="CallInvoker"/>,
    /// with the supplied interceptors applied in order. Useful for in-memory or test invokers.
    /// </summary>
    public static TClient CreateClient<TClient>(CallInvoker invoker, params Interceptor[] interceptors)
        where TClient : ClientBase
    {
        if (invoker == null)
            throw new ArgumentNullException(nameof(invoker));

        if (interceptors is { Length: > 0 })
            invoker = invoker.Intercept(interceptors);

        var client = Activator.CreateInstance(typeof(TClient), invoker);
        if (client == null)
            throw new InvalidOperationException($"Could not construct {typeof(TClient).Name} from a CallInvoker.");

        return (TClient)client;
    }
}
