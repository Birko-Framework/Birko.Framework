using Birko.Data.CosmosDB.Stores;
using Birko.Data.Models;
using FluentAssertions;
using Moq;
using Microsoft.Azure.Cosmos;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Birko.Data.CosmosDB.Tests;

public class TestModel : AbstractModel
{
    public string Name { get; set; } = string.Empty;
    public int Value { get; set; }
}

public class CosmosDBStoreTests
{
    [Fact]
    public void Constructor_Default_ShouldNotThrow()
    {
        var store = new CosmosDBStore<TestModel>();
        store.Should().NotBeNull();
        store.Container.Should().BeNull();
        store.Client.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithEmptyConnectionString_ShouldThrow()
    {
        var act = () => new CosmosDBStore<TestModel>("", "db");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_WithEmptyDatabaseName_ShouldThrow()
    {
        var act = () => new CosmosDBStore<TestModel>("AccountEndpoint=https://test.documents.azure.com:443/;AccountKey=dGVzdA==;", "");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_WithNullContainer_ShouldThrow()
    {
        var act = () => new CosmosDBStore<TestModel>((Container)null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Create_WithNullData_ShouldReturnEmptyGuid()
    {
        var store = new CosmosDBStore<TestModel>();
        var result = store.Create((TestModel)null!);
        result.Should().Be(Guid.Empty);
    }

    [Fact]
    public void Read_WithEmptyGuid_ShouldReturnNull()
    {
        var store = new CosmosDBStore<TestModel>();
        var result = store.Read(Guid.Empty);
        result.Should().BeNull();
    }

    [Fact]
    public void Read_WithNoContainer_ShouldReturnEmpty()
    {
        var store = new CosmosDBStore<TestModel>();
        var result = store.Read();
        result.Should().BeEmpty();
    }

    [Fact]
    public void Count_WithNoContainer_ShouldReturnZero()
    {
        var store = new CosmosDBStore<TestModel>();
        var result = store.Count();
        result.Should().Be(0);
    }

    [Fact]
    public void IsHealthy_WithNoClient_ShouldReturnFalse()
    {
        var store = new CosmosDBStore<TestModel>();
        store.IsHealthy().Should().BeFalse();
    }

    [Fact]
    public void PartitionKeyPath_Default_ShouldBeId()
    {
        CosmosDBStore<TestModel>.PartitionKeyPath.Should().Be("/id");
    }

    [Fact]
    public void RequestTimeout_Default_ShouldBe30Seconds()
    {
        CosmosDBStore<TestModel>.RequestTimeout.Should().Be(TimeSpan.FromSeconds(30));
    }
}
