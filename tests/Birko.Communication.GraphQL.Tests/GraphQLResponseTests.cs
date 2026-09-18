using Birko.Communication.GraphQL;
using Birko.Serialization.Json;
using FluentAssertions;
using Xunit;

namespace Birko.Communication.GraphQL.Tests;

public class GraphQLResponseTests
{
    private readonly SystemJsonSerializer _serializer = new();

    [Fact]
    public void Deserialize_Success_ReturnsData()
    {
        var json = """{"data":{"name":"Alice","age":30}}""";

        var response = GraphQLResponse<TestUser>.Deserialize(json, _serializer);

        response.Data.Should().NotBeNull();
        response.Data!.Name.Should().Be("Alice");
        response.Data.Age.Should().Be(30);
        response.HasErrors.Should().BeFalse();
    }

    [Fact]
    public void Deserialize_WithErrors_ReturnsErrors()
    {
        var json = """{"errors":[{"message":"Something went wrong","locations":[{"line":1,"column":5}]}]}""";

        var response = GraphQLResponse<TestUser>.Deserialize(json, _serializer);

        response.HasErrors.Should().BeTrue();
        response.Errors.Should().HaveCount(1);
        response.Errors![0].Message.Should().Be("Something went wrong");
        response.Errors![0].Locations.Should().HaveCount(1);
        response.Errors![0].Locations![0].Line.Should().Be(1);
        response.Errors![0].Locations![0].Column.Should().Be(5);
    }

    [Fact]
    public void Deserialize_WithNullData_DataIsNull()
    {
        var json = """{"data":null,"errors":[{"message":"Not found"}]}""";

        var response = GraphQLResponse<TestUser>.Deserialize(json, _serializer);

        response.Data.Should().BeNull();
        response.HasErrors.Should().BeTrue();
    }

    [Fact]
    public void Deserialize_WithExtensions_ReturnsExtensions()
    {
        var json = """{"data":{"name":"Bob"},"extensions":{"timing":{"total":42}}}""";

        var response = GraphQLResponse<TestUser>.Deserialize(json, _serializer);

        response.Extensions.Should().NotBeNull();
        response.Extensions!.ContainsKey("timing").Should().BeTrue();
    }

    [Fact]
    public void Deserialize_WithPath_ReturnsPath()
    {
        var json = """{"errors":[{"message":"err","path":["user","email"]}]}""";

        var response = GraphQLResponse<TestUser>.Deserialize(json, _serializer);

        response.Errors![0].Path.Should().ContainInOrder("user", "email");
    }

    public record TestUser(string Name, int Age);
}
