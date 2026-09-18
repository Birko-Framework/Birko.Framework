using Birko.Data.CosmosDB.Stores;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Birko.Data.CosmosDB.Tests;

public class AsyncCosmosDBStoreTests
{
    [Fact]
    public void Constructor_Default_ShouldNotThrow()
    {
        var store = new AsyncCosmosDBStore<TestModel>();
        store.Should().NotBeNull();
        store.Container.Should().BeNull();
        store.Client.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithEmptyConnectionString_ShouldThrow()
    {
        var act = () => new AsyncCosmosDBStore<TestModel>("", "db");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_WithNullContainer_ShouldThrow()
    {
        var act = () => new AsyncCosmosDBStore<TestModel>((Container)null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task CreateAsync_WithNullData_ShouldReturnEmptyGuid()
    {
        var store = new AsyncCosmosDBStore<TestModel>();
        var result = await store.CreateAsync((TestModel)null!);
        result.Should().Be(Guid.Empty);
    }

    [Fact]
    public async Task ReadAsync_WithEmptyGuid_ShouldReturnNull()
    {
        var store = new AsyncCosmosDBStore<TestModel>();
        var result = await store.ReadAsync(Guid.Empty);
        result.Should().BeNull();
    }

    [Fact]
    public async Task CountAsync_WithNoContainer_ShouldReturnZero()
    {
        var store = new AsyncCosmosDBStore<TestModel>();
        var result = await store.CountAsync();
        result.Should().Be(0);
    }

    [Fact]
    public async Task SaveAsync_WithNullData_ShouldReturnEmptyGuid()
    {
        var store = new AsyncCosmosDBStore<TestModel>();
        var result = await store.SaveAsync(null!);
        result.Should().Be(Guid.Empty);
    }

    [Fact]
    public void TransactionContext_Default_ShouldBeNull()
    {
        var store = new AsyncCosmosDBStore<TestModel>();
        store.TransactionContext.Should().BeNull();
    }

    [Fact]
    public void IsHealthy_WithNoClient_ShouldReturnFalse()
    {
        var store = new AsyncCosmosDBStore<TestModel>();
        store.IsHealthy().Should().BeFalse();
    }

    [Fact]
    public async Task ReadAsync_BulkWithNoContainer_ShouldReturnEmpty()
    {
        var store = new AsyncCosmosDBStore<TestModel>();
        var result = await store.ReadAsync(filter: null);
        result.Should().BeEmpty();
    }
}
