using Birko.Communication.GraphQL;
using FluentAssertions;
using Xunit;

namespace Birko.Communication.GraphQL.Tests;

public class GraphQLRequestBuilderTests
{
    [Fact]
    public void Query_SetsQueryField()
    {
        var request = new GraphQLRequestBuilder()
            .Query("{ users { name } }")
            .Build();

        request.Query.Should().Be("{ users { name } }");
    }

    [Fact]
    public void Mutation_SetsQueryField()
    {
        var request = new GraphQLRequestBuilder()
            .Mutation("mutation { createUser(name: \"A\") { id } }")
            .Build();

        request.Query.Should().Be("mutation { createUser(name: \"A\") { id } }");
    }

    [Fact]
    public void Variables_SetsVariables()
    {
        var vars = new { id = 1 };

        var request = new GraphQLRequestBuilder()
            .Query("query")
            .Variables(vars)
            .Build();

        request.Variables.Should().Be(vars);
    }

    [Fact]
    public void Variables_Dictionary_SetsVariablesDictionary()
    {
        var dict = new Dictionary<string, object?> { ["id"] = 1 };

        var request = new GraphQLRequestBuilder()
            .Query("query")
            .Variables(dict)
            .Build();

        request.VariablesDictionary.Should().BeSameAs(dict);
    }

    [Fact]
    public void OperationName_SetsName()
    {
        var request = new GraphQLRequestBuilder()
            .Query("query")
            .OperationName("GetUsers")
            .Build();

        request.OperationName.Should().Be("GetUsers");
    }

    [Fact]
    public void Build_WithoutQueryOrMutation_Throws()
    {
        var act = () => new GraphQLRequestBuilder().Build();

        act.Should().Throw<InvalidOperationException>().WithMessage("*Query*Mutation*");
    }

    [Fact]
    public void Build_WithBothQueryAndMutation_Throws()
    {
        var act = () => new GraphQLRequestBuilder()
            .Query("q")
            .Mutation("m")
            .Build();

        act.Should().Throw<InvalidOperationException>().WithMessage("*both*");
    }

    [Fact]
    public void WithExtension_AddsExtension()
    {
        var request = new GraphQLRequestBuilder()
            .Query("q")
            .WithExtension("key1", "value1")
            .Build();

        request.Extensions.Should().ContainKey("key1");
        request.Extensions!["key1"].Should().Be("value1");
    }

    [Fact]
    public void Fluent_Chaining_ReturnsSameBuilder()
    {
        var builder = new GraphQLRequestBuilder();

        builder.Query("q").Should().BeSameAs(builder);
        builder.Variables(new { }).Should().BeSameAs(builder);
        builder.OperationName("op").Should().BeSameAs(builder);
        builder.WithExtension("k", "v").Should().BeSameAs(builder);
    }
}
