using System;
using System.Collections.Generic;

namespace Birko.Communication.GraphQL;

/// <summary>
/// Fluent builder for constructing <see cref="GraphQLRequest"/> instances.
/// </summary>
public class GraphQLRequestBuilder
{
    private string? _query;
    private string? _mutation;
    private object? _variables;
    private Dictionary<string, object?>? _variablesDictionary;
    private string? _operationName;
    private Dictionary<string, object?>? _extensions;

    /// <summary>
    /// Sets the GraphQL query string.
    /// </summary>
    public GraphQLRequestBuilder Query(string query)
    {
        _query = query;
        return this;
    }

    /// <summary>
    /// Sets the GraphQL mutation string.
    /// </summary>
    public GraphQLRequestBuilder Mutation(string mutation)
    {
        _mutation = mutation;
        return this;
    }

    /// <summary>
    /// Sets variables as an anonymous object.
    /// </summary>
    public GraphQLRequestBuilder Variables(object variables)
    {
        _variables = variables;
        return this;
    }

    /// <summary>
    /// Sets variables as a dictionary.
    /// </summary>
    public GraphQLRequestBuilder Variables(Dictionary<string, object?> variables)
    {
        _variablesDictionary = variables;
        return this;
    }

    /// <summary>
    /// Sets the operation name for multi-operation documents.
    /// </summary>
    public GraphQLRequestBuilder OperationName(string name)
    {
        _operationName = name;
        return this;
    }

    /// <summary>
    /// Adds a single extension entry.
    /// </summary>
    public GraphQLRequestBuilder WithExtension(string key, object? value)
    {
        _extensions ??= new Dictionary<string, object?>();
        _extensions[key] = value;
        return this;
    }

    /// <summary>
    /// Builds the <see cref="GraphQLRequest"/>. Exactly one of Query or Mutation must be set.
    /// </summary>
    /// <exception cref="InvalidOperationException">Neither query nor mutation is set, or both are set.</exception>
    public GraphQLRequest Build()
    {
        var queryString = _query ?? _mutation;
        if (queryString is null)
            throw new InvalidOperationException("Either Query or Mutation must be set before building the request.");
        if (_query is not null && _mutation is not null)
            throw new InvalidOperationException("Cannot set both Query and Mutation on the same request.");

        return new GraphQLRequest
        {
            Query = queryString,
            Variables = _variables,
            VariablesDictionary = _variablesDictionary,
            OperationName = _operationName,
            Extensions = _extensions
        };
    }
}
