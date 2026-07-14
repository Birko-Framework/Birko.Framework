using System.Collections.Generic;
using Birko.Serialization;

namespace Birko.Communication.GraphQL;

/// <summary>
/// Represents a GraphQL request to be sent to the server.
/// </summary>
public class GraphQLRequest
{
    /// <summary>
    /// The GraphQL query or mutation string.
    /// </summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>
    /// Variables as an anonymous object (serialized via ISerializer).
    /// Use <see cref="VariablesDictionary"/> for dictionary-based variables.
    /// </summary>
    public object? Variables { get; set; }

    /// <summary>
    /// Variables as a dictionary (takes precedence over <see cref="Variables"/> when non-null).
    /// </summary>
    public Dictionary<string, object?>? VariablesDictionary { get; set; }

    /// <summary>
    /// The operation name for multi-operation documents.
    /// </summary>
    public string? OperationName { get; set; }

    /// <summary>
    /// Extensions payload (used for APQ hashes, custom metadata, etc.).
    /// </summary>
    public Dictionary<string, object?>? Extensions { get; set; }

    /// <summary>
    /// Serializes the request to a JSON string suitable for the GraphQL HTTP body,
    /// using the provided <see cref="ISerializer"/>.
    /// </summary>
    public string Serialize(ISerializer serializer)
    {
        var payload = new Dictionary<string, object?>
        {
            ["query"] = Query,
            ["variables"] = VariablesDictionary ?? Variables,
            ["operationName"] = OperationName,
            ["extensions"] = Extensions
        };

        return serializer.Serialize(payload);
    }
}
