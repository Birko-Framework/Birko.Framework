using Birko.Data.Aggregates.Mapping;
using Birko.Data.Aggregates.Tests.TestResources;
using Birko.Data.Models;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Birko.Data.Aggregates.Tests;

/// <summary>
/// SH-H014 (TASK-309): <c>AggregateMapper.ExpandCollection</c> handled <c>ManyToMany</c> with the same code
/// as <c>OneToMany</c>, tagging every operation with <c>relationship.ChildType</c> and the <b>child entity
/// itself</b>. <c>JunctionType</c> / <c>JunctionParentFk</c> / <c>JunctionChildFk</c> are set by
/// <c>RelationshipBuilder.Through</c> and were read <b>nowhere</b> in the mapper, while
/// <c>IAggregateMapper.Expand</c>'s own doc promises "insert/delete operations for child <b>and junction
/// table</b> entities".
/// <para>So removing one category from a product emitted <c>Delete</c> of the shared <c>Category</c> row —
/// a caller applying the operation destroys a row every other product shares — and adding an existing
/// category emitted <c>Insert</c> of a <c>Category</c> that already exists.</para>
/// <para>A many-to-many association <i>is</i> the junction row, and its identity is the
/// (parentFk, childFk) pair. Both the insert and the delete therefore carry a junction instance with exactly
/// those two properties set and a null Guid.</para>
/// </summary>
public class AggregateMapperJunctionExpandTests
{
    private const string Nav = nameof(Product.Categories);

    private static (AggregateMapper<Product> Mapper, Product Root, InMemoryRelatedDataProvider Provider) Fixture()
    {
        var root = new Product { Guid = Guid.NewGuid(), Name = "Widget" };
        return (new AggregateMapper<Product>(new ProductAggregate()), root, new InMemoryRelatedDataProvider());
    }

    private static FlattenResult<Product> Aggregate(Product root, IEnumerable<AbstractModel> desiredCategories)
    {
        var result = new FlattenResult<Product>(root);
        result.NestedCollections[Nav] = desiredCategories.ToList();
        return result;
    }

    // ------------------------------------------------------------- the defect

    [Fact]
    public void Removing_a_category_deletes_the_junction_row_not_the_shared_category()
    {
        var (mapper, root, provider) = Fixture();
        var category = new Category { Guid = Guid.NewGuid(), Name = "Tools" };
        provider.SetJunctionRelated(root.Guid!.Value, Nav, [category]);

        var operations = mapper.Expand(Aggregate(root, []), provider).ToList();

        var op = operations.Should().ContainSingle().Subject;
        op.Type.Should().Be(SyncOperationType.Delete);

        // The whole finding: this used to be typeof(Category) carrying the Category instance, so a caller
        // applying the operation deleted a row shared with every other product.
        op.EntityType.Should().Be<ProductCategory>();
        op.Entity.Should().BeOfType<ProductCategory>();

        var junction = (ProductCategory)op.Entity;
        junction.ProductGuid.Should().Be(root.Guid);
        junction.CategoryGuid.Should().Be(category.Guid);
        junction.Guid.Should().BeNull("the junction row is identified by its two foreign keys");
    }

    [Fact]
    public void Adding_an_existing_category_inserts_a_junction_row_not_a_duplicate_category()
    {
        var (mapper, root, provider) = Fixture();
        var category = new Category { Guid = Guid.NewGuid(), Name = "Tools" };
        provider.SetJunctionRelated(root.Guid!.Value, Nav, []);

        var operations = mapper.Expand(Aggregate(root, [category]), provider).ToList();

        var op = operations.Should().ContainSingle().Subject;
        op.Type.Should().Be(SyncOperationType.Insert);
        op.EntityType.Should().Be<ProductCategory>();

        var junction = op.Entity.Should().BeOfType<ProductCategory>().Subject;
        junction.ProductGuid.Should().Be(root.Guid);
        junction.CategoryGuid.Should().Be(category.Guid);
    }

    [Fact]
    public void Swapping_one_category_for_another_emits_two_junction_operations()
    {
        var (mapper, root, provider) = Fixture();
        var removed = new Category { Guid = Guid.NewGuid(), Name = "Old" };
        var added = new Category { Guid = Guid.NewGuid(), Name = "New" };
        provider.SetJunctionRelated(root.Guid!.Value, Nav, [removed]);

        var operations = mapper.Expand(Aggregate(root, [added]), provider).ToList();

        operations.Should().HaveCount(2);
        operations.Should().OnlyContain(o => o.EntityType == typeof(ProductCategory));

        operations.Single(o => o.Type == SyncOperationType.Insert).Entity
            .Should().BeOfType<ProductCategory>().Which.CategoryGuid.Should().Be(added.Guid);
        operations.Single(o => o.Type == SyncOperationType.Delete).Entity
            .Should().BeOfType<ProductCategory>().Which.CategoryGuid.Should().Be(removed.Guid);
    }

    [Fact]
    public void An_unchanged_category_emits_nothing()
    {
        var (mapper, root, provider) = Fixture();
        var category = new Category { Guid = Guid.NewGuid(), Name = "Tools" };
        provider.SetJunctionRelated(root.Guid!.Value, Nav, [category]);

        mapper.Expand(Aggregate(root, [category]), provider)
            .Where(o => o.NavigationProperty == Nav)
            .Should().BeEmpty();
    }

    [Fact]
    public async Task ExpandAsync_produces_the_same_junction_operations()
    {
        // Expand and ExpandAsync share ExpandCollection, but only a test says so — and this is the twin
        // that a future edit is most likely to fork.
        var (mapper, root, provider) = Fixture();
        var category = new Category { Guid = Guid.NewGuid(), Name = "Tools" };
        provider.SetJunctionRelated(root.Guid!.Value, Nav, [category]);

        var operations = (await mapper.ExpandAsync(Aggregate(root, []), provider, CancellationToken.None)).ToList();

        var op = operations.Should().ContainSingle().Subject;
        op.Type.Should().Be(SyncOperationType.Delete);
        op.EntityType.Should().Be<ProductCategory>();
        op.Entity.Should().BeOfType<ProductCategory>()
            .Which.CategoryGuid.Should().Be(category.Guid);
    }

    // ------------------------------------------------------------ the refusal

    [Fact]
    public void A_desired_category_with_no_Guid_is_refused_rather_than_half_written()
    {
        // A junction row cannot reference a child that has no key. The OneToMany path treats a Guid-less
        // child as an unconditional insert (CR-H041), but here that would emit a child insert with NO
        // association — exactly the silent half-write this finding is about. So it refuses, and the message
        // names the remedy.
        var (mapper, root, provider) = Fixture();
        provider.SetJunctionRelated(root.Guid!.Value, Nav, []);

        Action act = () => mapper.Expand(Aggregate(root, [new Category { Name = "Unsaved" }]), provider).ToList();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Categories*")
            .WithMessage("*no Guid*");
    }

    [Fact]
    public void Through_constrains_its_junction_type_to_AbstractModel()
    {
        // Structural pin. SH-H014 made the mapper materialise junction rows and hand them to
        // SyncOperation, whose Entity is an AbstractModel — so Through<TJunction> gained a
        // `where TJunction : AbstractModel` constraint. Drop it and the builder starts accepting what
        // JunctionOperation must then refuse at runtime, which is the two-answers-for-one-question shape
        // this codebase keeps paying for. Nothing else in the suite notices, which is why this exists.
        var through = typeof(Core.RelationshipBuilder<Product, Category>)
            .GetMethod(nameof(Core.RelationshipBuilder<Product, Category>.Through))!;

        var junction = through.GetGenericArguments().Single();
        junction.GetGenericParameterConstraints().Should().ContainSingle()
            .Which.Should().Be<AbstractModel>();
    }

    // --------------------------------------------- contract pins (not evidence)

    [Fact]
    public void A_one_to_many_child_is_still_emitted_as_the_child_itself()
    {
        // Tags are Via(...), not Through(...). The junction treatment must not leak into direct FK
        // collections — without this, "always emit a junction row" would pass every test above.
        var (mapper, root, provider) = Fixture();
        var tag = new Tag { Guid = Guid.NewGuid(), Name = "sale" };
        provider.SetDirectRelated(root.Guid!.Value, nameof(Product.Tags), [tag]);

        var aggregate = new FlattenResult<Product>(root);
        aggregate.NestedCollections[nameof(Product.Tags)] = [];

        var op = mapper.Expand(aggregate, provider)
            .Single(o => o.NavigationProperty == nameof(Product.Tags));

        op.Type.Should().Be(SyncOperationType.Delete);
        op.EntityType.Should().Be<Tag>();
        op.Entity.Should().BeSameAs(tag);
    }

    [Fact]
    public void A_one_to_many_child_with_no_Guid_is_still_an_unconditional_insert()
    {
        // CR-H041's behaviour, unchanged: only the many-to-many path refuses a keyless child.
        var (mapper, root, provider) = Fixture();
        provider.SetDirectRelated(root.Guid!.Value, nameof(Product.Tags), []);

        var aggregate = new FlattenResult<Product>(root);
        aggregate.NestedCollections[nameof(Product.Tags)] = [new Tag { Name = "new" }];

        var op = mapper.Expand(aggregate, provider)
            .Single(o => o.NavigationProperty == nameof(Product.Tags));

        op.Type.Should().Be(SyncOperationType.Insert);
        op.EntityType.Should().Be<Tag>();
    }
}
