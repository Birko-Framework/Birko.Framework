using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Communication.GraphQL;

/// <summary>
/// Event args for GraphQL request events.
/// </summary>
public class GraphQLRequestEventArgs : EventArgs
{
    public string Query { get; init; } = string.Empty;
    public string? OperationName { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Event args for GraphQL response events.
/// </summary>
public class GraphQLResponseEventArgs : EventArgs
{
    public string Query { get; init; } = string.Empty;
    public int StatusCode { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Event args for GraphQL error events.
/// </summary>
public class GraphQLErrorEventArgs : EventArgs
{
    public IReadOnlyList<GraphQLError> Errors { get; init; } = Array.Empty<GraphQLError>();
    public int? StatusCode { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// GraphQL client interface for executing queries, mutations, and subscriptions.
/// </summary>
public interface IGraphQLClient : IDisposable
{
    /// <summary>
    /// Executes a GraphQL query.
    /// </summary>
    Task<GraphQLResponse<T>> QueryAsync<T>(string query, object? variables = null, string? operationName = null, CancellationToken ct = default);

    /// <summary>
    /// Executes a GraphQL mutation.
    /// </summary>
    Task<GraphQLResponse<T>> MutateAsync<T>(string mutation, object? variables = null, string? operationName = null, CancellationToken ct = default);

    /// <summary>
    /// Subscribes to a GraphQL subscription via WebSocket.
    /// Requires <see cref="GraphQLSettings.UseSubscriptions"/> to be true.
    /// </summary>
    Task<IGraphQLSubscription<T>> SubscribeAsync<T>(string subscription, object? variables = null, string? operationName = null, CancellationToken ct = default);

    /// <summary>
    /// Executes a raw <see cref="GraphQLRequest"/> and returns the typed response.
    /// </summary>
    Task<GraphQLResponse<T>> ExecuteAsync<T>(GraphQLRequest request, CancellationToken ct = default);

    /// <summary>
    /// Fired before a GraphQL request is sent.
    /// </summary>
    event EventHandler<GraphQLRequestEventArgs>? OnRequest;

    /// <summary>
    /// Fired after a successful GraphQL response is received.
    /// </summary>
    event EventHandler<GraphQLResponseEventArgs>? OnResponse;

    /// <summary>
    /// Fired when a GraphQL response contains errors.
    /// </summary>
    event EventHandler<GraphQLErrorEventArgs>? OnError;
}
