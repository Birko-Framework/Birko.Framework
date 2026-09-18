using Birko.Data.Aggregates.Mapping;
using Birko.Data.Aggregates.Tests.TestResources;
using FluentAssertions;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Birko.Data.Aggregates.Tests;

public class AggregateMapperFlattenTests
{
    private readonly ProductAggregate _definition = new();
    private readonly AggregateMapper<Product> _mapper;
    private readonly InMemoryRelatedDataProvider _provider = new();

    private static readonly Guid ProductGuid = Guid.NewGuid();
    private static readonly Guid Cat1Guid = Guid.NewGuid();
    private static readonly Guid Cat2Guid = Guid.NewGuid();
    private static readonly Guid Tag1Guid = Guid.NewGuid();
    private static readonly Guid ImageGuid = Guid.NewGuid();

    public AggregateMapperFlattenTests()
    {
        _mapper = new AggregateMapper<Product>(_definition);
    }

    private Product CreateProduct() => new() { Guid = ProductGuid, Name = "Widget" };

    private void SetupRelatedData()
    {
        _provider.SetJunctionRelated(ProductGuid, "Categories", new Category[]
        {
            new() { Guid = Cat1Guid, Name = "Electronics" },
            new() { Guid = Cat2Guid, Name = "Gadgets" }
        });

        _provider.SetDirectRelated(ProductGuid, "Tags", new Tag[]
        {
            new() { Guid = Tag1Guid, Name = "new", ProductGuid = ProductGuid }
        });

        _provider.SetDirectRelated(ProductGuid, "DefaultImage", new Image[]
        {
            new() { Guid = ImageGuid, Url = "img.png", ProductGuid = ProductGuid }
        });
    }

    // --- Flatten basics ---

    [Fact]
    public void Flatten_SetsRootGuid()
    {
        var product = CreateProduct();
        var result = _mapper.Flatten(product, _provider);

        result.RootGuid.Should().Be(ProductGuid);
        result.Root.Should().BeSameAs(product);
    }

    [Fact]
    public void Flatten_WithNoRelatedData_ReturnsEmptyCollections()
    {
        var result = _mapper.Flatten(CreateProduct(), _provider);

        result.NestedCollections["Categories"].Should().BeEmpty();
        result.NestedCollections["Tags"].Should().BeEmpty();
        result.NestedSingles["DefaultImage"].Should().BeNull();
    }

    [Fact]
    public void Flatten_ResolvesAllRelationships()
    {
        SetupRelatedData();
        var result = _mapper.Flatten(CreateProduct(), _provider);

        result.NestedCollections["Categories"].Should().HaveCount(2);
        result.NestedCollections["Tags"].Should().HaveCount(1);
        result.NestedSingles["DefaultImage"].Should().NotBeNull();
    }

    // --- GetCollection / GetSingle typed accessors ---

    [Fact]
    public void Flatten_GetCollection_ReturnsCastCategories()
    {
        SetupRelatedData();
        var result = _mapper.Flatten(CreateProduct(), _provider);

        var categories = result.GetCollection<Category>("Categories");
        categories.Should().NotBeNull();
        categories!.Should().HaveCount(2);
        categories!.First().Name.Should().Be("Electronics");
    }

    [Fact]
    public void Flatten_GetSingle_ReturnsCastImage()
    {
        SetupRelatedData();
        var result = _mapper.Flatten(CreateProduct(), _provider);

        var image = result.GetSingle<Image>("DefaultImage");
        image.Should().NotBeNull();
        image!.Url.Should().Be("img.png");
    }

    [Fact]
    public void Flatten_GetCollection_UnknownProperty_ReturnsNull()
    {
        var result = _mapper.Flatten(CreateProduct(), _provider);
        result.GetCollection<Category>("NonExistent").Should().BeNull();
    }

    [Fact]
    public void Flatten_GetSingle_UnknownProperty_ReturnsNull()
    {
        var result = _mapper.Flatten(CreateProduct(), _provider);
        result.GetSingle<Image>("NonExistent").Should().BeNull();
    }

    // --- FlattenMany ---

    [Fact]
    public void FlattenMany_FlattensTwoProducts()
    {
        var p1Guid = Guid.NewGuid();
        var p2Guid = Guid.NewGuid();
        var products = new[]
        {
            new Product { Guid = p1Guid, Name = "A" },
            new Product { Guid = p2Guid, Name = "B" }
        };

        var results = _mapper.FlattenMany(products, _provider).ToList();
        results.Should().HaveCount(2);
        results[0].RootGuid.Should().Be(p1Guid);
        results[1].RootGuid.Should().Be(p2Guid);
    }

    // --- Async ---

    [Fact]
    public async Task FlattenAsync_ResolvesAllRelationships()
    {
        SetupRelatedData();
        var result = await _mapper.FlattenAsync(CreateProduct(), _provider);

        result.NestedCollections["Categories"].Should().HaveCount(2);
        result.NestedSingles["DefaultImage"].Should().NotBeNull();
    }

    [Fact]
    public async Task FlattenManyAsync_FlattensTwoProducts()
    {
        var products = new[]
        {
            new Product { Guid = Guid.NewGuid(), Name = "A" },
            new Product { Guid = Guid.NewGuid(), Name = "B" }
        };

        var results = (await _mapper.FlattenManyAsync(products, _provider)).ToList();
        results.Should().HaveCount(2);
    }

    // --- Validation ---

    [Fact]
    public void Flatten_NullRoot_Throws()
    {
        var act = () => _mapper.Flatten(null!, _provider);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Flatten_NullProvider_Throws()
    {
        var act = () => _mapper.Flatten(CreateProduct(), null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Flatten_RootWithNullGuid_Throws()
    {
        var product = new Product { Guid = null, Name = "No Guid" };
        var act = () => _mapper.Flatten(product, _provider);
        act.Should().Throw<ArgumentException>().WithMessage("*non-null Guid*");
    }

    [Fact]
    public void Constructor_DefinitionTypeMismatch_Throws()
    {
        var orderDef = new OrderAggregate();
        var act = () => new AggregateMapper<Product>(orderDef);
        act.Should().Throw<ArgumentException>().WithMessage("*does not match*");
    }
}
