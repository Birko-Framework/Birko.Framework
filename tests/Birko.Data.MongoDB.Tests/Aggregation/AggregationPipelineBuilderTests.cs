using Birko.Data.MongoDB.Aggregation;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using System;
using Xunit;

namespace Birko.Data.MongoDB.Tests.Aggregation;

public class TestDocument
{
    public string Name { get; set; } = string.Empty;
    public int Value { get; set; }
}

public class AggregationPipelineBuilderTests
{
    private static AggregationPipelineBuilder<TestDocument> CreateBuilder()
    {
        var mockCollection = new Mock<IMongoCollection<TestDocument>>();
        return new AggregationPipelineBuilder<TestDocument>(mockCollection.Object);
    }

    [Fact]
    public void Constructor_WithValidCollection_CreatesBuilder()
    {
        var builder = CreateBuilder();

        builder.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_NullCollection_ThrowsArgumentNullException()
    {
        var act = () => new AggregationPipelineBuilder<TestDocument>(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Limit_ReturnsSameBuilder()
    {
        var builder = CreateBuilder();

        var result = builder.Limit(10);

        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void Skip_ReturnsSameBuilder()
    {
        var builder = CreateBuilder();

        var result = builder.Skip(5);

        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void Group_ReturnsSameBuilder()
    {
        var builder = CreateBuilder();
        var groupExpr = new BsonDocument("_id", "$Name");

        var result = builder.Group(groupExpr);

        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void Group_NullExpression_ReturnsSameBuilder()
    {
        var builder = CreateBuilder();

        var result = builder.Group(null!);

        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void ProjectBsonDocument_ReturnsSameBuilder()
    {
        var builder = CreateBuilder();
        var projection = new BsonDocument("Name", 1);

        var result = builder.Project(projection);

        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void ProjectBsonDocument_Null_ReturnsSameBuilder()
    {
        var builder = CreateBuilder();

        var result = builder.Project((BsonDocument)null!);

        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void Unwind_ByName_ReturnsSameBuilder()
    {
        var builder = CreateBuilder();

        var result = builder.Unwind("items");

        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void Unwind_EmptyName_ReturnsSameBuilder()
    {
        var builder = CreateBuilder();

        var result = builder.Unwind("");

        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void Lookup_ReturnsSameBuilder()
    {
        var builder = CreateBuilder();

        var result = builder.Lookup("orders", "customerId", "_id", "customerOrders");

        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void Lookup_EmptyFrom_ReturnsSameBuilder()
    {
        var builder = CreateBuilder();

        var result = builder.Lookup("", "localField", "foreignField", "as");

        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void Count_ReturnsSameBuilder()
    {
        var builder = CreateBuilder();

        var result = builder.Count("total");

        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void Count_EmptyFieldName_ReturnsSameBuilder()
    {
        var builder = CreateBuilder();

        var result = builder.Count("");

        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void AddFields_ReturnsSameBuilder()
    {
        var builder = CreateBuilder();
        var fields = new BsonDocument("computed", new BsonDocument("$multiply", new BsonArray { "$price", "$quantity" }));

        var result = builder.AddFields(fields);

        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void AddFields_Null_ReturnsSameBuilder()
    {
        var builder = CreateBuilder();

        var result = builder.AddFields(null!);

        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void FluentChaining_MultipleStages_ReturnsSameBuilder()
    {
        var builder = CreateBuilder();

        var result = builder
            .Group(new BsonDocument("_id", "$Name"))
            .Limit(10)
            .Skip(5);

        result.Should().BeSameAs(builder);
    }
}
