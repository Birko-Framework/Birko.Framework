using System;
using System.Collections.Generic;

namespace Birko.Communication.GraphQL;

/// <summary>
/// Exception thrown when a GraphQL operation fails.
/// </summary>
public class GraphQLException : Exception
{
    /// <summary>
    /// The errors returned by the GraphQL server, if any.
    /// </summary>
    public IReadOnlyList<GraphQLError>? Errors { get; }

    /// <summary>
    /// The HTTP status code of the response, if applicable.
    /// </summary>
    public int? StatusCode { get; }

    public GraphQLException(string message) : base(message)
    {
    }

    public GraphQLException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public GraphQLException(string message, IReadOnlyList<GraphQLError>? errors, int? statusCode = null)
        : base(message)
    {
        Errors = errors;
        StatusCode = statusCode;
    }

    public GraphQLException(string message, IReadOnlyList<GraphQLError>? errors, int? statusCode, Exception innerException)
        : base(message, innerException)
    {
        Errors = errors;
        StatusCode = statusCode;
    }
}
