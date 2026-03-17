using Birko.Data.Aggregates.Mapping;
using Birko.Data.Aggregates.Tests.TestResources;
using FluentAssertions;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Birko.Data.Aggregates.Tests;

public class AggregateMapperExpandTests
{
    private readonly OrderAggregate _definition = new();
    private readonly AggregateMapper<Order> _mapper;

    private static readonly Guid OrderGuid = Guid.NewGuid();
    private static readonly Guid Line1Guid = Guid.NewGuid();
    private static readonly Guid Line2Guid = Guid.NewGuid();
    private static readonly Guid Line3Guid = Guid.NewGuid();
    private static readonly Guid CustomerGuid = Guid.NewGuid();

    public AggregateMapperExpandTests()
    {
        _mapper = new AggregateMapper<Order>(_definition);
    }

    private Order CreateOrder() => new() { Guid = OrderGuid, OrderNumber = "ORD-001" };

    // --- Expand: no changes ---

    [Fact]
    public void Expand_NoChanges_ReturnsEmpty()
    {
        var currentProvider = new InMemoryRelatedDataProvider();
        currentProvider.SetDirectRelated(OrderGuid, "Lines", new OrderLine[]
        {
            new() { Guid = Line1Guid, OrderGuid = OrderGuid, Quantity = 2 }
        });
        currentProvider.SetDirectRelated(OrderGuid, "Customer", new Customer[]
        {
            new() { Guid = CustomerGuid, Email = "a@b.com", OrderGuid = OrderGuid }
        });

        // Build aggregate with same state
        var aggregate = new FlattenResult<Order>(CreateOrder());
        aggregate.NestedCollections["Lines"] = new OrderLine[]
        {
            new() { Guid = Line1Guid, OrderGuid = OrderGuid, Quantity = 2 }
        };
        aggregate.NestedSingles["Customer"] = new Customer
        {
            Guid = CustomerGuid, Email = "a@b.com", OrderGuid = OrderGuid
        };

        var ops = _mapper.Expand(aggregate, currentProvider).ToList();
        ops.Should().BeEmpty();
    }

    // --- Expand: collection additions ---

    [Fact]
    public void Expand_NewLineAdded_GeneratesInsert()
    {
        var currentProvider = new InMemoryRelatedDataProvider();
        currentProvider.SetDirectRelated(OrderGuid, "Lines", new OrderLine[]
        {
            new() { Guid = Line1Guid, OrderGuid = OrderGuid, Quantity = 1 }
        });
        currentProvider.SetDirectRelated(OrderGuid, "Customer", Array.Empty<Customer>());

        var aggregate = new FlattenResult<Order>(CreateOrder());
        aggregate.NestedCollections["Lines"] = new OrderLine[]
        {
            new() { Guid = Line1Guid, OrderGuid = OrderGuid, Quantity = 1 },
            new() { Guid = Line2Guid, OrderGuid = OrderGuid, Quantity = 3 }
        };
        aggregate.NestedSingles["Customer"] = null;

        var ops = _mapper.Expand(aggregate, currentProvider).ToList();
        ops.Should().HaveCount(1);
        ops[0].Type.Should().Be(SyncOperationType.Insert);
        ops[0].EntityType.Should().Be(typeof(OrderLine));
        ops[0].Entity.Guid.Should().Be(Line2Guid);
        ops[0].NavigationProperty.Should().Be("Lines");
    }

    // --- Expand: collection removals ---

    [Fact]
    public void Expand_LineRemoved_GeneratesDelete()
    {
        var currentProvider = new InMemoryRelatedDataProvider();
        currentProvider.SetDirectRelated(OrderGuid, "Lines", new OrderLine[]
        {
            new() { Guid = Line1Guid, OrderGuid = OrderGuid, Quantity = 1 },
            new() { Guid = Line2Guid, OrderGuid = OrderGuid, Quantity = 3 }
        });
        currentProvider.SetDirectRelated(OrderGuid, "Customer", Array.Empty<Customer>());

        var aggregate = new FlattenResult<Order>(CreateOrder());
        aggregate.NestedCollections["Lines"] = new OrderLine[]
        {
            new() { Guid = Line1Guid, OrderGuid = OrderGuid, Quantity = 1 }
        };
        aggregate.NestedSingles["Customer"] = null;

        var ops = _mapper.Expand(aggregate, currentProvider).ToList();
        ops.Should().HaveCount(1);
        ops[0].Type.Should().Be(SyncOperationType.Delete);
        ops[0].Entity.Guid.Should().Be(Line2Guid);
    }

    // --- Expand: mixed add + remove ---

    [Fact]
    public void Expand_MixedAddRemove_GeneratesBothOperations()
    {
        var currentProvider = new InMemoryRelatedDataProvider();
        currentProvider.SetDirectRelated(OrderGuid, "Lines", new OrderLine[]
        {
            new() { Guid = Line1Guid, OrderGuid = OrderGuid, Quantity = 1 },
            new() { Guid = Line2Guid, OrderGuid = OrderGuid, Quantity = 2 }
        });
        currentProvider.SetDirectRelated(OrderGuid, "Customer", Array.Empty<Customer>());

        var aggregate = new FlattenResult<Order>(CreateOrder());
        aggregate.NestedCollections["Lines"] = new OrderLine[]
        {
            new() { Guid = Line1Guid, OrderGuid = OrderGuid, Quantity = 1 },
            new() { Guid = Line3Guid, OrderGuid = OrderGuid, Quantity = 5 }
        };
        aggregate.NestedSingles["Customer"] = null;

        var ops = _mapper.Expand(aggregate, currentProvider).ToList();
        ops.Should().HaveCount(2);
        ops.Should().Contain(o => o.Type == SyncOperationType.Insert && o.Entity.Guid == Line3Guid);
        ops.Should().Contain(o => o.Type == SyncOperationType.Delete && o.Entity.Guid == Line2Guid);
    }

    // --- Expand: OneToOne addition ---

    [Fact]
    public void Expand_CustomerAdded_GeneratesInsert()
    {
        var currentProvider = new InMemoryRelatedDataProvider();
        currentProvider.SetDirectRelated(OrderGuid, "Lines", Array.Empty<OrderLine>());
        currentProvider.SetDirectRelated(OrderGuid, "Customer", Array.Empty<Customer>());

        var aggregate = new FlattenResult<Order>(CreateOrder());
        aggregate.NestedCollections["Lines"] = Array.Empty<OrderLine>();
        aggregate.NestedSingles["Customer"] = new Customer
        {
            Guid = CustomerGuid, Email = "new@test.com", OrderGuid = OrderGuid
        };

        var ops = _mapper.Expand(aggregate, currentProvider).ToList();
        ops.Should().HaveCount(1);
        ops[0].Type.Should().Be(SyncOperationType.Insert);
        ops[0].EntityType.Should().Be(typeof(Customer));
    }

    // --- Expand: OneToOne removal ---

    [Fact]
    public void Expand_CustomerRemoved_GeneratesDelete()
    {
        var currentProvider = new InMemoryRelatedDataProvider();
        currentProvider.SetDirectRelated(OrderGuid, "Lines", Array.Empty<OrderLine>());
        currentProvider.SetDirectRelated(OrderGuid, "Customer", new Customer[]
        {
            new() { Guid = CustomerGuid, Email = "old@test.com", OrderGuid = OrderGuid }
        });

        var aggregate = new FlattenResult<Order>(CreateOrder());
        aggregate.NestedCollections["Lines"] = Array.Empty<OrderLine>();
        aggregate.NestedSingles["Customer"] = null;

        var ops = _mapper.Expand(aggregate, currentProvider).ToList();
        ops.Should().HaveCount(1);
        ops[0].Type.Should().Be(SyncOperationType.Delete);
        ops[0].Entity.Guid.Should().Be(CustomerGuid);
    }

    // --- Expand: OneToOne replacement ---

    [Fact]
    public void Expand_CustomerReplaced_GeneratesDeleteAndInsert()
    {
        var newCustomerGuid = Guid.NewGuid();
        var currentProvider = new InMemoryRelatedDataProvider();
        currentProvider.SetDirectRelated(OrderGuid, "Lines", Array.Empty<OrderLine>());
        currentProvider.SetDirectRelated(OrderGuid, "Customer", new Customer[]
        {
            new() { Guid = CustomerGuid, Email = "old@test.com", OrderGuid = OrderGuid }
        });

        var aggregate = new FlattenResult<Order>(CreateOrder());
        aggregate.NestedCollections["Lines"] = Array.Empty<OrderLine>();
        aggregate.NestedSingles["Customer"] = new Customer
        {
            Guid = newCustomerGuid, Email = "new@test.com", OrderGuid = OrderGuid
        };

        var ops = _mapper.Expand(aggregate, currentProvider).ToList();
        ops.Should().HaveCount(2);
        ops.Should().Contain(o => o.Type == SyncOperationType.Delete && o.Entity.Guid == CustomerGuid);
        ops.Should().Contain(o => o.Type == SyncOperationType.Insert && o.Entity.Guid == newCustomerGuid);
    }

    // --- Async ---

    [Fact]
    public async Task ExpandAsync_GeneratesCorrectOperations()
    {
        var currentProvider = new InMemoryRelatedDataProvider();
        currentProvider.SetDirectRelated(OrderGuid, "Lines", Array.Empty<OrderLine>());
        currentProvider.SetDirectRelated(OrderGuid, "Customer", Array.Empty<Customer>());

        var aggregate = new FlattenResult<Order>(CreateOrder());
        aggregate.NestedCollections["Lines"] = new OrderLine[]
        {
            new() { Guid = Line1Guid, OrderGuid = OrderGuid, Quantity = 1 }
        };
        aggregate.NestedSingles["Customer"] = null;

        var ops = (await _mapper.ExpandAsync(aggregate, currentProvider)).ToList();
        ops.Should().HaveCount(1);
        ops[0].Type.Should().Be(SyncOperationType.Insert);
    }
}
