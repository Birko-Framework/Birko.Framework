using System.Collections.Generic;
using System.Text.Json;

namespace Birko.Communication.GraphQL;

/// <summary>
/// Represents a single error returned in a GraphQL response.
/// </summary>
public class GraphQLError
{
    /// <summary>
    /// The error message.
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// Source locations (line/column) where the error occurred.
    /// </summary>
    public List<GraphQLLocation>? Locations { get; init; }

    /// <summary>
    /// Path to the field that caused the error (e.g., ["user", "email"]).
    /// </summary>
    public List<string>? Path { get; init; }

    /// <summary>
    /// Additional error details provided by the server.
    /// </summary>
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

/// <summary>
/// A source location (line and column) for a GraphQL error.
/// </summary>
public class GraphQLLocation
{
    /// <summary>
    /// The 1-based line number.
    /// </summary>
    public int Line { get; init; }

    /// <summary>
    /// The 1-based column number.
    /// </summary>
    public int Column { get; init; }
}
