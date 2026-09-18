using Birko.Configuration;

namespace Birko.Communication.gRPC.Server;

/// <summary>
/// Server-side gRPC configuration applied to <c>Grpc.AspNetCore</c> via
/// <see cref="GrpcServiceExtensions.AddBirkoGrpc"/>.
/// </summary>
public class GrpcServerSettings : Settings
{
    /// <summary>
    /// Whether to include exception detail in error responses. Useful in development;
    /// leave false in production to avoid leaking internals. Default false.
    /// </summary>
    public bool EnableDetailedErrors { get; set; }

    /// <summary>
    /// Maximum size in bytes of a received message. Null = library default (4 MB).
    /// </summary>
    public int? MaxReceiveMessageSizeBytes { get; set; }

    /// <summary>
    /// Maximum size in bytes of a sent message. Null = unlimited (library default).
    /// </summary>
    public int? MaxSendMessageSizeBytes { get; set; }

    /// <summary>
    /// Whether to enable the gRPC server reflection endpoint. The host must add the
    /// <c>Grpc.AspNetCore.Server.Reflection</c> package and map the reflection service;
    /// this flag is the intent signal honored by the host. Default false.
    /// </summary>
    public bool EnableReflection { get; set; }
}
