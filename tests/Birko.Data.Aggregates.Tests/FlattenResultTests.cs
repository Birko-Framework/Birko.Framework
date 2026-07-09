using System;
using System.Collections.Generic;
using System.Linq;
using Birko.Data.Aggregates.Mapping;
using Birko.Data.Aggregates.Tests.TestResources;
using Birko.Data.Models;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Aggregates.Tests;

/// <summary>
/// Regression for CR-H040: GetCollection used `collection as IEnumerable&lt;TChild&gt;`, which
/// returned null for a provider that materializes results to List&lt;AbstractModel&gt; (not
/// reference-assignable to IEnumerable&lt;TChild&gt;). It now projects with OfType and returns items.
/// </summary>
public class FlattenResultTests
{
    [Fact]
    public void GetCollection_MaterializedListOfAbstractModel_ReturnsItems()
    {
        var order = new Order { Guid = Guid.NewGuid(), OrderNumber = "ORD-1" };
        var result = new FlattenResult<Order>(order);

        // A realistic provider projection: List<AbstractModel>, NOT the concrete OrderLine[].
        var lines = new List<AbstractModel>
        {
            new OrderLine { Guid = Guid.NewGuid(), Quantity = 1 },
            new OrderLine { Guid = Guid.NewGuid(), Quantity = 2 },
        };
        result.NestedCollections["Lines"] = lines;

        var collection = result.GetCollection<OrderLine>("Lines");

        collection.Should().NotBeNull("CR-H040: a List<AbstractModel> must not silently yield null");
        collection!.Should().HaveCount(2);
        collection!.Select(l => l.Quantity).Should().BeEquivalentTo(new[] { 1, 2 });
    }

    [Fact]
    public void GetCollection_CovariantArray_StillReturnsItems()
    {
        var order = new Order { Guid = Guid.NewGuid(), OrderNumber = "ORD-2" };
        var result = new FlattenResult<Order>(order);
        result.NestedCollections["Lines"] = new OrderLine[] { new() { Guid = Guid.NewGuid(), Quantity = 7 } };

        result.GetCollection<OrderLine>("Lines")!.Single().Quantity.Should().Be(7);
    }

    [Fact]
    public void GetCollection_MissingKey_ReturnsNull()
    {
        var result = new FlattenResult<Order>(new Order { Guid = Guid.NewGuid(), OrderNumber = "x" });

        result.GetCollection<OrderLine>("Nope").Should().BeNull();
    }
}
