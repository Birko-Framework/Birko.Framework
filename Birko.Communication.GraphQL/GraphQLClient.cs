using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Birko.Serialization.Json;

namespace Birko.Communication.GraphQL;

/// <summary>
/// GraphQL client implementing <see cref="IGraphQLClient"/>.
/// Uses <see cref="HttpClient"/> for queries/mutations and <see cref="ClientWebSocket"/> for subscriptions.
/// Zero external dependencies.
/// </summary>
public class GraphQLClient : IGraphQLClient
{
    private static readonly Dictionary<string, GraphQLClient> _clients = new();
    private static readonly SemaphoreSlim _cacheLock = new(1, 1);

    private readonly GraphQLSettings _settings;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly SystemJsonSerializer _serializer = new();
    private readonly SemaphoreSlim _requestLock = new(1, 1);

    // Subscription state
    private readonly SemaphoreSlim _subscriptionLock = new(1, 1);
    private int _subscriptionCounter;

    /// <inheritdoc />
    public event EventHandler<GraphQLRequestEventArgs>? OnRequest;

    /// <inheritdoc />
    public event EventHandler<GraphQLResponseEventArgs>? OnResponse;

    /// <inheritdoc />
    public event EventHandler<GraphQLErrorEventArgs>? OnError;

    /// <summary>
    /// Creates a new GraphQL client with the specified settings.
    /// </summary>
    public GraphQLClient(GraphQLSettings settings) : this(settings, null) { }

    /// <summary>
    /// Creates a new GraphQL client with optional HttpClient injection.
    /// </summary>
    public GraphQLClient(GraphQLSettings settings, HttpClient? httpClient)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        if (string.IsNullOrWhiteSpace(_settings.Endpoint))
            throw new ArgumentException("GraphQL endpoint URL is required.", nameof(settings));

        if (httpClient is not null)
        {
            _httpClient = httpClient;
            _ownsHttpClient = false;
        }
        else
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(_settings.TimeoutSeconds) };
            _ownsHttpClient = true;
        }
    }

    /// <summary>
    /// Gets or creates a cached <see cref="GraphQLClient"/> for the given endpoint URL.
    /// </summary>
    public static GraphQLClient GetClient(string endpoint)
    {
        _cacheLock.Wait();
        try
        {
            if (!_clients.TryGetValue(endpoint, out var client))
            {
                var settings = new GraphQLSettings { Endpoint = endpoint };
                client = new GraphQLClient(settings);
                _clients[endpoint] = client;
            }

            return client;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    /// <summary>
    /// Removes and disposes the cached client for the given endpoint.
    /// </summary>
    public static bool RemoveClient(string endpoint)
    {
        _cacheLock.Wait();
        try
        {
            if (_clients.Remove(endpoint, out var client))
            {
                client.Dispose();
                return true;
            }

            return false;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    /// <summary>
    /// Disposes all cached clients and clears the cache.
    /// </summary>
    public static void ClearCache()
    {
        _cacheLock.Wait();
        try
        {
            foreach (var client in _clients.Values)
                client.Dispose();
            _clients.Clear();
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    /// <inheritdoc />
    public Task<GraphQLResponse<T>> QueryAsync<T>(string query, object? variables = null, string? operationName = null, CancellationToken ct = default)
    {
        var request = new GraphQLRequest
        {
            Query = query,
            Variables = variables,
            OperationName = operationName
        };
        return ExecuteAsync<T>(request, ct);
    }

    /// <inheritdoc />
    public Task<GraphQLResponse<T>> MutateAsync<T>(string mutation, object? variables = null, string? operationName = null, CancellationToken ct = default)
    {
        var request = new GraphQLRequest
        {
            Query = mutation,
            Variables = variables,
            OperationName = operationName
        };
        return ExecuteAsync<T>(request, ct);
    }

    /// <inheritdoc />
    public async Task<GraphQLResponse<T>> ExecuteAsync<T>(GraphQLRequest request, CancellationToken ct = default)
    {
        await _requestLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            OnRequest?.Invoke(this, new GraphQLRequestEventArgs
            {
                Query = request.Query,
                OperationName = request.OperationName
            });

            var json = request.Serialize(_serializer);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _settings.Endpoint) { Content = content };

            foreach (var header in _settings.ExtraHeaders)
                httpRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);

            using var response = await _httpClient.SendAsync(httpRequest, ct).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            var result = GraphQLResponse<T>.Deserialize(responseBody, _serializer);

            if (result.HasErrors)
            {
                OnError?.Invoke(this, new GraphQLErrorEventArgs
                {
                    Errors = result.Errors!,
                    StatusCode = (int)response.StatusCode
                });

                throw new GraphQLException(
                    result.Errors![0].Message,
                    result.Errors,
                    (int)response.StatusCode);
            }

            OnResponse?.Invoke(this, new GraphQLResponseEventArgs
            {
                Query = request.Query,
                StatusCode = (int)response.StatusCode
            });

            return result;
        }
        finally
        {
            _requestLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IGraphQLSubscription<T>> SubscribeAsync<T>(string subscription, object? variables = null, string? operationName = null, CancellationToken ct = default)
    {
        if (!_settings.UseSubscriptions)
            throw new InvalidOperationException("Subscriptions are not enabled. Set UseSubscriptions = true in GraphQLSettings.");

        await _subscriptionLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var wsUri = ToWebSocketUri(_settings.Endpoint);
            var webSocket = new ClientWebSocket();
            webSocket.Options.AddSubProtocol("graphql-transport-ws");

            foreach (var header in _settings.ExtraHeaders)
                webSocket.Options.SetRequestHeader(header.Key, header.Value);

            await webSocket.ConnectAsync(new Uri(wsUri), ct).ConfigureAwait(false);

            // Send connection_init
            var initMsg = JsonSerializer.Serialize(new { type = "connection_init" });
            var initBytes = Encoding.UTF8.GetBytes(initMsg);
            await webSocket.SendAsync(new ArraySegment<byte>(initBytes), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);

            // Wait for connection_ack, tolerating ping / keep-alive frames that a server may send
            // first, and accumulating fragmented/large frames until complete (CR-H021).
            string? ackType = null;
            while (true)
            {
                var ackMsg = await WebSocketMessageReader.ReceiveTextAsync(webSocket, ct).ConfigureAwait(false);
                if (ackMsg.Type == WebSocketMessageType.Close || ackMsg.Text == null)
                {
                    webSocket.Dispose();
                    throw new GraphQLException("WebSocket closed before connection_ack");
                }

                using var ackDoc = JsonDocument.Parse(ackMsg.Text);
                ackType = ackDoc.RootElement.TryGetProperty("type", out var at) ? at.GetString() : null;

                if (ackType == "ping")
                {
                    var pong = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { type = "pong" }));
                    await webSocket.SendAsync(new ArraySegment<byte>(pong), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
                    continue;
                }
                if (ackType == "ka") // legacy keep-alive
                    continue;

                break;
            }

            if (ackType != "connection_ack")
            {
                webSocket.Dispose();
                throw new GraphQLException($"Expected connection_ack, received: {ackType}");
            }

            // Start subscription. The negotiated subprotocol is graphql-transport-ws, whose
            // client->server start message type is "subscribe" (not the legacy "start") — CR-H020.
            var subscriptionId = $"sub_{Interlocked.Increment(ref _subscriptionCounter)}";
            var startPayload = new
            {
                type = "subscribe",
                id = subscriptionId,
                payload = new
                {
                    query = subscription,
                    variables = variables,
                    operationName = operationName
                }
            };
            var startJson = JsonSerializer.Serialize(startPayload);
            var startBytes = Encoding.UTF8.GetBytes(startJson);
            await webSocket.SendAsync(new ArraySegment<byte>(startBytes), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);

            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var gqlSubscription = new GraphQLSubscription<T>(webSocket, subscriptionId, cts);

            // Fire and forget the receive loop
            _ = Task.Run(async () =>
            {
                try
                {
                    await gqlSubscription.StartReceivingAsync().ConfigureAwait(false);
                }
                catch
                {
                    // Receive loop ended — subscription is disposed or cancelled
                }
            }, cts.Token);

            return gqlSubscription;
        }
        finally
        {
            _subscriptionLock.Release();
        }
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
        _requestLock.Dispose();
        _subscriptionLock.Dispose();
    }

    private static string ToWebSocketUri(string httpUri)
    {
        if (httpUri.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return "wss://" + httpUri["https://".Length..];
        if (httpUri.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            return "ws://" + httpUri["http://".Length..];
        return httpUri;
    }
}
