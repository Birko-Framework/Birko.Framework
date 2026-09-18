using System.Collections.Generic;
using Birko.Configuration;

namespace Birko.Communication.GraphQL;

/// <summary>
/// Subscription transport protocol.
/// </summary>
public enum GraphQLSubscriptionProtocol
{
    /// <summary>
    /// WebSocket-based subscriptions using graphql-ws protocol.
    /// </summary>
    WebSocket,

    /// <summary>
    /// Server-Sent Events-based subscriptions.
    /// </summary>
    SSE
}

/// <summary>
/// GraphQL client settings extending <see cref="RemoteSettings"/> for endpoint connectivity.
/// <para>
/// Location = GraphQL endpoint URL (e.g., "https://api.example.com/graphql").
/// </para>
/// </summary>
public class GraphQLSettings : RemoteSettings
{
    /// <summary>
    /// The GraphQL endpoint URL — alias for <see cref="RemoteSettings.Location"/>.
    /// </summary>
    public string Endpoint
    {
        get => Location ?? string.Empty;
        set => Location = value;
    }

    /// <summary>
    /// Path component appended to the base URL (default "/graphql").
    /// <para><b>Reserved — not yet implemented (CR-L051):</b> the client uses <see cref="Endpoint"/>
    /// verbatim as the POST/WS URL and does not currently append this path.</para>
    /// </summary>
    public string SchemaPath { get; set; } = "/graphql";

    /// <summary>
    /// Whether subscriptions are enabled. Must be true to use <see cref="IGraphQLClient.SubscribeAsync{T}"/>.
    /// </summary>
    public bool UseSubscriptions { get; set; }

    /// <summary>
    /// The transport protocol for subscriptions. Default is <see cref="GraphQLSubscriptionProtocol.WebSocket"/>.
    /// <para><b>Reserved — not yet implemented (CR-L051):</b> the client always uses the WebSocket
    /// (graphql-transport-ws) transport regardless of this value.</para>
    /// </summary>
    public GraphQLSubscriptionProtocol SubscriptionProtocol { get; set; } = GraphQLSubscriptionProtocol.WebSocket;

    /// <summary>
    /// Timeout in seconds for HTTP requests. Default is 30.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Whether to enable Automatic Persisted Queries (APQ).
    /// <para><b>Reserved — not yet implemented (CR-L051):</b> the client does not currently populate the
    /// request <c>extensions</c> for APQ; this flag is not read.</para>
    /// </summary>
    public bool EnableAutoPersistedQueries { get; set; }

    /// <summary>
    /// Extra HTTP headers to include with every request.
    /// </summary>
    public Dictionary<string, string> ExtraHeaders { get; set; } = new();
}
