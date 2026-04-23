using Birko.Communication.GraphQL;
using Birko.Serialization.Json;
using FluentAssertions;
using System.Text.Json;
using Xunit;

namespace Birko.Communication.GraphQL.Tests;

public class GraphQLRequestTests
{
    private readonly SystemJsonSerializer _serializer = new();

    [Fact]
    public void Constructor_SetsQuery()
    {
        var request = new GraphQLRequest { Query = "{ users { name } }" };

        request.Query.Should().Be("{ users { name } }");
    }

    [Fact]
    public void Constructor_SetsVariables()
    {
        var variables = new { id = 42 };
        var request = new GraphQLRequest { Query = "query", Variables = variables };

        request.Variables.Should().Be(variables);
    }

    [Fact]
    public void Constructor_SetsOperationName()
    {
        var request = new GraphQLRequest { Query = "query", OperationName = "GetUsers" };

        request.OperationName.Should().Be("GetUsers");
    }

    [Fact]
    public void Serialize_ProducesCorrectJson()
    {
        var request = new GraphQLRequest
        {
            Query = "{ users { name } }",
            Variables = new { id = 1 },
            OperationName = "GetUsers"
        };

        var json = request.Serialize(_serializer);

        json.Should().Contain("\"query\"");
        json.Should().Contain("\"variables\"");
        json.Should().Contain("\"operationName\"");
        json.Should().Contain("GetUsers");

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("query").GetString().Should().Be("{ users { name } }");
        doc.RootElement.GetProperty("operationName").GetString().Should().Be("GetUsers");
    }

    [Fact]
    public void Serialize_WithNullVariables_KeepsNullVariables()
    {
        var request = new GraphQLRequest { Query = "query" };

        var json = request.Serialize(_serializer);

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("variables", out _).Should().BeTrue();
    }

    [Fact]
    public void Serialize_WithExtensions_IncludesExtensions()
    {
        var request = new GraphQLRequest
        {
            Query = "query",
            Extensions = new Dictionary<string, object?> { ["persistedQuery"] = new { version = 1 } }
        };

        var json = request.Serialize(_serializer);

        json.Should().Contain("persistedQuery");
    }

    [Fact]
    public void Serialize_VariablesDictionary_TakesPrecedence()
    {
        var request = new GraphQLRequest
        {
            Query = "query",
            Variables = new { id = 1 },
            VariablesDictionary = new Dictionary<string, object?> { ["id"] = 99 }
        };

        var json = request.Serialize(_serializer);

        json.Should().Contain("99");
        json.Should().NotContain("\"id\":1}");
    }
}
