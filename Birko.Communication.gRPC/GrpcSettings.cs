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
    /// Optional per-call deadline in seconds.
    /// <para><b>Reserved — not yet consumed (CR-L054):</b> no code currently reads this; to enforce a
    /// deadline, set <c>CallOptions.Deadline</c> on the call (e.g. via a custom interceptor).</para>
    /// </summary>
    public int? DeadlineSeconds { get; set; }

    /// <summary>
    /// Explicit channel credentials. When null, the channel is created by
    /// <see cref="Grpc.Net.Client.GrpcChannel.ForAddress(string)"/>, which infers credentials from the
    /// endpoint scheme (https → transport security, http → insecure) — the pool does no extra inference.
    /// </summary>
    public ChannelCredentials? Credentials { get; set; }

    /// <summary>
    /// Extra metadata (headers) to attach to outgoing calls.
    /// <para><b>Reserved — not auto-applied (CR-L055):</b> this settings property is not read by the
    /// factory/pool. To attach static metadata, pass it to <see cref="GrpcAuthenticationInterceptor"/>
    /// explicitly (its <c>extraMetadata</c> constructor parameter) when creating the client.</para>
    /// </summary>
    public Dictionary<string, string> ExtraMetadata { get; set; } = new();
}
