using System.Collections.Generic;
using Birko.Configuration;
using Grpc.Core;

namespace Birko.Communication.gRPC;

/// <summary>
/// gRPC client settings extending <see cref="RemoteSettings"/> for channel connectivity.
/// <para>
/// Location = the gRPC endpoint address (e.g., "https://api.example.com:443").
/// </para>
/// </summary>
public class GrpcSettings : RemoteSettings
{
    /// <summary>
    /// The gRPC endpoint address — alias for <see cref="RemoteSettings.Location"/>.
    /// Should be an absolute URL whose scheme (http/https) selects the transport security.
    /// </summary>
    public string Endpoint
    {
        get => Location ?? string.Empty;
        set => Location = value;
    }

    /// <summary>
    /// Maximum size in bytes of a message the channel may receive. Null = library default (4 MB).
    /// </summary>
    public int? MaxReceiveMessageSizeBytes { get; set; }

    /// <summary>
    /// Maximum size in bytes of a message the channel may send. Null = unlimited (library default).
    /// </summary>
    public int? MaxSendMessageSizeBytes { get; set; }

    /// <summary>
    /// Optional per-call deadline in seconds, applied by interceptors that honor it. Null = no deadline.
    /// </summary>
    public int? DeadlineSeconds { get; set; }

    /// <summary>
    /// Explicit channel credentials. When null, credentials are inferred from the endpoint scheme
    /// (https → <see cref="ChannelCredentials.SecureSsl"/>, http → <see cref="ChannelCredentials.Insecure"/>).
    /// </summary>
    public ChannelCredentials? Credentials { get; set; }

    /// <summary>
    /// Extra metadata (headers) attached to every outgoing call by the authentication interceptor.
    /// </summary>
    public Dictionary<string, string> ExtraMetadata { get; set; } = new();
}
