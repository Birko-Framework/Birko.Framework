using Birko.Data.Aggregates.Extensions;
using Birko.Data.Aggregates.Mapping;
using Birko.Data.Aggregates.Tests.TestResources;
using FluentAssertions;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Birko.Data.Aggregates.Tests;

public class SyncPipelineExtensionsTests
{
    private static readonly Guid OrderGuid = Guid.NewGuid();
    private static readonly Guid LineGuid = Guid.NewGuid();

    // --- CreateMapper ---

    [Fact]
    public void CreateMapper_ReturnsAggregateMapper()
    {
        var definition = new OrderAggregate();
        var mapper = definition.CreateMapper();

        mapper.Should().NotBeNull();
        mapper.Should().BeOfType<AggregateMapper<Order>>();
    }

    // --- FlattenForSync ---

    [Fact]
    public void FlattenForSync_FlattensEntities()
    {
        var definition = new OrderAggregate();
        var mapper = definition.CreateMapper();
        var provider = new InMemoryRelatedDataProvider();

        var orders = new[]
        {
            new Order { Guid = Guid.NewGuid(), OrderNumber = "A" },
            new Order { Guid = Guid.NewGuid(), OrderNumber = "B" }
        };

        var results = mapper.FlattenForSync(orders, provider).ToList();
        results.Should().HaveCount(2);
    }

    // --- FlattenForSyncAsync ---

    [Fact]
    public async Task FlattenForSyncAsync_FlattensEntities()
    {
        var mapper = new OrderAggregate().CreateMapper();
        var provider = new InMemoryRelatedDataProvider();

        var orders = new[]
        {
            new Order { Guid = Guid.NewGuid(), OrderNumber = "A" }
        };

        var results = (await mapper.FlattenForSyncAsync(orders, provider)).ToList();
        results.Should().HaveCount(1);
    }

    // --- ExpandFromSync ---

    [Fact]
    public void ExpandFromSync_GeneratesOperations()
    {
        var mapper = new OrderAggregate().CreateMapper();
        var currentProvider = new InMemoryRelatedDataProvider();
        currentProvider.SetDirectRelated(OrderGuid, "Lines", Array.Empty<OrderLine>());
        currentProvider.SetDirectRelated(OrderGuid, "Customer", Array.Empty<Customer>());

        var aggregate = new FlattenResult<Order>(new Order { Guid = OrderGuid, OrderNumber = "X" });
        aggregate.NestedCollections["Lines"] = new OrderLine[]
        {
            new() { Guid = LineGuid, OrderGuid = OrderGuid, Quantity = 1 }
        };
        aggregate.NestedSingles["Customer"] = null;

        var ops = mapper.ExpandFromSync(aggregate, currentProvider).ToList();
        ops.Should().HaveCount(1);
        ops[0].Type.Should().Be(SyncOperationType.Insert);
    }

    // --- ExpandManyFromSync ---

    [Fact]
    public void ExpandManyFromSync_ExpandsMultipleAggregates()
    {
        var mapper = new OrderAggregate().CreateMapper();
        var provider = new InMemoryRelatedDataProvider();

        var guid1 = Guid.NewGuid();
        var guid2 = Guid.NewGuid();
        var line1 = Guid.NewGuid();
        var line2 = Guid.NewGuid();

        provider.SetDirectRelated(guid1, "Lines", Array.Empty<OrderLine>());
        provider.SetDirectRelated(guid1, "Customer", Array.Empty<Customer>());
        provider.SetDirectRelated(guid2, "Lines", Array.Empty<OrderLine>());
        provider.SetDirectRelated(guid2, "Customer", Array.Empty<Customer>());

        var agg1 = new FlattenResult<Order>(new Order { Guid = guid1, OrderNumber = "1" });
        agg1.NestedCollections["Lines"] = new OrderLine[] { new() { Guid = line1, Quantity = 1 } };
        agg1.NestedSingles["Customer"] = null;

        var agg2 = new FlattenResult<Order>(new Order { Guid = guid2, OrderNumber = "2" });
        agg2.NestedCollections["Lines"] = new OrderLine[] { new() { Guid = line2, Quantity = 2 } };
        agg2.NestedSingles["Customer"] = null;

        var ops = mapper.ExpandManyFromSync(new[] { agg1, agg2 }, provider).ToList();
        ops.Should().HaveCount(2);
        ops.Should().OnlyContain(o => o.Type == SyncOperationType.Insert);
    }

    // --- ExpandManyFromSyncAsync ---

    [Fact]
    public async Task ExpandManyFromSyncAsync_ExpandsMultipleAggregates()
    {
        var mapper = new OrderAggregate().CreateMapper();
        var provider = new InMemoryRelatedDataProvider();

        var guid1 = Guid.NewGuid();
        provider.SetDirectRelated(guid1, "Lines", Array.Empty<OrderLine>());
        provider.SetDirectRelated(guid1, "Customer", Array.Empty<Customer>());

        var agg = new FlattenResult<Order>(new Order { Guid = guid1, OrderNumber = "1" });
        agg.NestedCollections["Lines"] = new OrderLine[] { new() { Guid = Guid.NewGuid(), Quantity = 1 } };
        agg.NestedSingles["Customer"] = null;

        var ops = (await mapper.ExpandManyFromSyncAsync(new[] { agg }, provider)).ToList();
        ops.Should().HaveCount(1);
    }
}
