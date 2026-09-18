using Birko.Communication.GraphQL;
using FluentAssertions;
using Xunit;

namespace Birko.Communication.GraphQL.Tests;

public class GraphQLExceptionTests
{
    [Fact]
    public void Constructor_Message_SetsMessage()
    {
        var ex = new GraphQLException("Test error");

        ex.Message.Should().Be("Test error");
        ex.Errors.Should().BeNull();
        ex.StatusCode.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithInnerException_SetsInner()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new GraphQLException("Test", inner);

        ex.InnerException.Should().Be(inner);
    }

    [Fact]
    public void Constructor_WithErrors_SetsProperties()
    {
        var errors = new List<GraphQLError>
        {
            new() { Message = "Field not found" }
        };

        var ex = new GraphQLException("GraphQL error", errors, 400);

        ex.Errors.Should().HaveCount(1);
        ex.Errors![0].Message.Should().Be("Field not found");
        ex.StatusCode.Should().Be(400);
    }

    [Fact]
    public void Constructor_WithErrorsAndInner_SetsAll()
    {
        var inner = new Exception("inner");
        var errors = new List<GraphQLError> { new() { Message = "err" } };

        var ex = new GraphQLException("msg", errors, 500, inner);

        ex.Errors.Should().HaveCount(1);
        ex.StatusCode.Should().Be(500);
        ex.InnerException.Should().Be(inner);
    }
}
