using System;
using Grpc.Core;

namespace Birko.Communication.gRPC;

/// <summary>
/// Exception wrapping a gRPC <see cref="RpcException"/>, exposing the status code and detail
/// in a Birko-friendly shape (mirrors <c>GraphQLException</c> / <c>OAuthException</c>).
/// </summary>
public class GrpcException : Exception
{
    /// <summary>
    /// The gRPC status code returned by the call.
    /// </summary>
    public StatusCode StatusCode { get; }

    /// <summary>
    /// The status detail message returned by the server.
    /// </summary>
    public string Detail { get; }

    /// <summary>
    /// The trailing metadata returned with the failed call, if any.
    /// </summary>
    public Metadata? Trailers { get; }

    public GrpcException(StatusCode statusCode, string detail, Metadata? trailers = null, Exception? innerException = null)
        : base($"gRPC call failed with status {statusCode}: {detail}", innerException)
    {
        StatusCode = statusCode;
        Detail = detail;
        Trailers = trailers;
    }

    /// <summary>
    /// Wraps an <see cref="RpcException"/> into a <see cref="GrpcException"/>.
    /// </summary>
    public static GrpcException FromRpcException(RpcException exception)
    {
        if (exception == null)
            throw new ArgumentNullException(nameof(exception));

        return new GrpcException(
            exception.StatusCode,
            exception.Status.Detail ?? string.Empty,
            exception.Trailers,
            exception);
    }
}
