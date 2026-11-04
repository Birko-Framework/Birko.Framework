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
    public void Create_WithNullData_IsAnArgumentError()
    {
        // TASK-714 (inverted): a null create answered Guid.Empty, a write reported that did not happen.
        var store = new CosmosDBStore<TestModel>();
        store.Invoking(s => s.Create((TestModel)null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Read_WithEmptyGuid_IsRefused()
    {
        var store = new CosmosDBStore<TestModel>();

        // TASK-812 (rule 56, inverted): a store that was never configured refuses, reads included; it answered empty.
        FluentActions.Invoking(() => store.Read(Guid.Empty)).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Read_WithNoContainer_IsRefused()
    {
        var store = new CosmosDBStore<TestModel>();

        // TASK-812 (rule 56, inverted): a store that was never configured refuses, reads included; it answered empty.
        FluentActions.Invoking(() => store.Read()).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Count_WithNoContainer_IsRefused()
    {
        var store = new CosmosDBStore<TestModel>();

        // TASK-812 (rule 56, inverted): a store that was never configured refuses, reads included; it answered empty.
        FluentActions.Invoking(() => store.Count()).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void IsHealthy_WithNoClient_ShouldReturnFalse()
    {
        var store = new CosmosDBStore<TestModel>();
        store.IsHealthy().Should().BeFalse();
    }

    [Fact]
    public void Settings_PartitionKeyPath_Default_ShouldBeId()
    {
        var settings = new Birko.Data.CosmosDB.Stores.Settings();
        settings.PartitionKeyPath.Should().Be("/id");
    }

    [Fact]
    public void Settings_RequestTimeout_Default_ShouldBe30Seconds()
    {
        var settings = new Birko.Data.CosmosDB.Stores.Settings();
        settings.RequestTimeout.Should().Be(TimeSpan.FromSeconds(30));
    }
}
