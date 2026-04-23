using System.Collections.Generic;
using System.Text.Json;
using Birko.Serialization;

namespace Birko.Communication.GraphQL;

/// <summary>
/// Represents a typed GraphQL response with data, errors, and extensions.
/// </summary>
public class GraphQLResponse<T>
{
    /// <summary>
    /// The data returned by the GraphQL operation, or default if null/absent.
    /// </summary>
    public T? Data { get; set; }

    /// <summary>
    /// Errors returned by the GraphQL server. Non-null when the operation encountered errors.
    /// </summary>
    public List<GraphQLError>? Errors { get; set; }

    /// <summary>
    /// Extensions returned by the server (timing, tracing, etc.).
    /// </summary>
    public Dictionary<string, JsonElement>? Extensions { get; set; }

    /// <summary>
    /// Whether the response contains errors.
    /// </summary>
    public bool HasErrors => Errors is { Count: > 0 };

    /// <summary>
    /// Deserializes a GraphQL JSON response into a typed <see cref="GraphQLResponse{T}"/>
    /// using the provided <see cref="ISerializer"/>.
    /// </summary>
    public static GraphQLResponse<T> Deserialize(string json, ISerializer serializer)
    {
        return serializer.Deserialize<GraphQLResponse<T>>(json)
            ?? new GraphQLResponse<T>();
    }
}
