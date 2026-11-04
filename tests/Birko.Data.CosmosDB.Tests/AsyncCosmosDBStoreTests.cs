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
    public async Task CreateAsync_WithNullData_IsAnArgumentError()
    {
        // TASK-714 (inverted): a null create answered Guid.Empty, a write reported that did not happen.
        var store = new AsyncCosmosDBStore<TestModel>();
        await store.Invoking(s => s.CreateAsync((TestModel)null!)).Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ReadAsync_WithEmptyGuid_IsRefused()
    {
        var store = new AsyncCosmosDBStore<TestModel>();

        // TASK-812 (rule 56, inverted): a store that was never configured refuses, reads included; it answered empty.
        await FluentActions.Awaiting(() => store.ReadAsync(Guid.Empty)).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CountAsync_WithNoContainer_IsRefused()
    {
        var store = new AsyncCosmosDBStore<TestModel>();

        // TASK-812 (rule 56, inverted): a store that was never configured refuses, reads included; it answered empty.
        await FluentActions.Awaiting(() => store.CountAsync()).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SaveAsync_WithNullData_IsRefused()
    {
        var store = new AsyncCosmosDBStore<TestModel>();

        // TASK-812 (rule 56, inverted): a store that was never configured refuses, reads included; it answered empty.
        await FluentActions.Awaiting(() => store.SaveAsync(null!)).Should().ThrowAsync<InvalidOperationException>();
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
    public async Task ReadAsync_BulkWithNoContainer_IsRefused()
    {
        var store = new AsyncCosmosDBStore<TestModel>();

        // TASK-812 (rule 56, inverted): a store that was never configured refuses, reads included; it answered empty.
        await FluentActions.Awaiting(() => store.ReadAsync(filter: null)).Should().ThrowAsync<InvalidOperationException>();
    }
}
