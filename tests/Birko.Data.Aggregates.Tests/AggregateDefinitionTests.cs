using Birko.Data.Aggregates.Core;
using Birko.Data.Aggregates.Tests.TestResources;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Aggregates.Tests;

public class AggregateDefinitionTests
{
    // --- RootType ---

    [Fact]
    public void ProductAggregate_RootType_IsProduct()
    {
        var def = new ProductAggregate();
        def.RootType.Should().Be(typeof(Product));
    }

    [Fact]
    public void OrderAggregate_RootType_IsOrder()
    {
        var def = new OrderAggregate();
        def.RootType.Should().Be(typeof(Order));
    }

    // --- Relationship count ---

    [Fact]
    public void ProductAggregate_HasThreeRelationships()
    {
        var def = new ProductAggregate();
        def.Relationships.Should().HaveCount(3);
    }

    [Fact]
    public void OrderAggregate_HasTwoRelationships()
    {
        var def = new OrderAggregate();
        def.Relationships.Should().HaveCount(2);
    }

    // --- HasMany with Through (ManyToMany) ---

    [Fact]
    public void ProductAggregate_Categories_IsManyToMany()
    {
        var def = new ProductAggregate();
        var rel = def.Relationships[0];

        rel.NavigationProperty.Should().Be("Categories");
        rel.Type.Should().Be(RelationshipType.ManyToMany);
        rel.ParentType.Should().Be(typeof(Product));
        rel.ChildType.Should().Be(typeof(Category));
        rel.JunctionType.Should().Be(typeof(ProductCategory));
        rel.JunctionParentFk.Should().Be("ProductGuid");
        rel.JunctionChildFk.Should().Be("CategoryGuid");
        rel.ForeignKeyProperty.Should().BeNull();
    }

    // --- HasMany with Via (OneToMany) ---

    [Fact]
    public void ProductAggregate_Tags_IsOneToMany()
    {
        var def = new ProductAggregate();
        var rel = def.Relationships[1];

        rel.NavigationProperty.Should().Be("Tags");
        rel.Type.Should().Be(RelationshipType.OneToMany);
        rel.ChildType.Should().Be(typeof(Tag));
        rel.ForeignKeyProperty.Should().Be("ProductGuid");
        rel.JunctionType.Should().BeNull();
    }

    // --- HasOne with Via (OneToOne) ---

    [Fact]
    public void ProductAggregate_DefaultImage_IsOneToOne()
    {
        var def = new ProductAggregate();
        var rel = def.Relationships[2];

        rel.NavigationProperty.Should().Be("DefaultImage");
        rel.Type.Should().Be(RelationshipType.OneToOne);
        rel.ChildType.Should().Be(typeof(Image));
        rel.ForeignKeyProperty.Should().Be("ProductGuid");
        rel.JunctionType.Should().BeNull();
    }

    // --- OrderAggregate ---

    [Fact]
    public void OrderAggregate_Lines_IsOneToMany()
    {
        var def = new OrderAggregate();
        var rel = def.Relationships[0];

        rel.NavigationProperty.Should().Be("Lines");
        rel.Type.Should().Be(RelationshipType.OneToMany);
        rel.ChildType.Should().Be(typeof(OrderLine));
        rel.ForeignKeyProperty.Should().Be("OrderGuid");
    }

    [Fact]
    public void OrderAggregate_Customer_IsOneToOne()
    {
        var def = new OrderAggregate();
        var rel = def.Relationships[1];

        rel.NavigationProperty.Should().Be("Customer");
        rel.Type.Should().Be(RelationshipType.OneToOne);
        rel.ChildType.Should().Be(typeof(Customer));
        rel.ForeignKeyProperty.Should().Be("OrderGuid");
    }
}
