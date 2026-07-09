using System;
using System.Collections.Generic;
using System.Linq;
using Birko.Models;
using Birko.Models.Product;
using Birko.Models.Product.Filters;
using FluentAssertions;
using Xunit;
using ProductModel = global::Birko.Models.Product.Product;

namespace Birko.Models.Product.Tests;

/// <summary>
/// CR-H131: ProductList.Filter() combined lambda bodies without rebinding parameters, so any
/// two-clause filter (Search+Category, Search+Tags, Category+Properties) threw
/// InvalidOperationException on Compile()/translation. These tests compile the predicate and
/// execute it, which is exactly what used to blow up.
/// </summary>
public class ProductListFilterTests
{
    private sealed class TestProduct : ProductModel, IProductTags, IProductProperties
    {
        public IEnumerable<SourceValue<string>> Tags { get; set; } = Array.Empty<SourceValue<string>>();
        public IEnumerable<SourceValue<string>> Properties { get; set; } = Array.Empty<SourceValue<string>>();
    }

    private static TestProduct Make(string name, string category, (string src, string val)[]? tags = null, (string src, string val)[]? props = null)
        => new()
        {
            Name = name,
            Category = category,
            Tags = (tags ?? Array.Empty<(string, string)>()).Select(t => new SourceValue<string> { Source = t.src, Value = t.val }).ToArray(),
            Properties = (props ?? Array.Empty<(string, string)>()).Select(t => new SourceValue<string> { Source = t.src, Value = t.val }).ToArray(),
        };

    private static Func<TestProduct, bool> Compile(ProductList<TestProduct> list)
    {
        var expr = list.Filter();
        expr.Should().NotBeNull();
        return expr!.Compile();
    }

    [Fact]
    public void SearchPlusCategory_CompilesAndMatches()
    {
        var list = new ProductList<TestProduct>(search: "Widget", category: "Tools");
        var predicate = Compile(list);

        predicate(Make("Widget Pro", "Tools/Hand")).Should().BeTrue();
        predicate(Make("Widget Pro", "Toys")).Should().BeFalse("category must also match");
        predicate(Make("Gadget", "Tools/Hand")).Should().BeFalse("name must also match");
    }

    [Fact]
    public void SearchPlusTags_CompilesAndMatches()
    {
        var tags = new Dictionary<string, List<string>> { ["color"] = new() { "red" } };
        var list = new ProductList<TestProduct>(search: "Widget", tags: tags);
        var predicate = Compile(list);

        predicate(Make("Widget", "X", tags: new[] { ("color", "red") })).Should().BeTrue();
        predicate(Make("Widget", "X", tags: new[] { ("color", "blue") })).Should().BeFalse();
        predicate(Make("Gadget", "X", tags: new[] { ("color", "red") })).Should().BeFalse();
    }

    [Fact]
    public void CategoryPlusProperties_CompilesAndMatches()
    {
        var props = new Dictionary<string, List<string>> { ["size"] = new() { "L" } };
        var list = new ProductList<TestProduct>(category: "Apparel", parameters: props);
        var predicate = Compile(list);

        predicate(Make("Shirt", "Apparel/Tops", props: new[] { ("size", "L") })).Should().BeTrue();
        predicate(Make("Shirt", "Apparel/Tops", props: new[] { ("size", "S") })).Should().BeFalse();
        predicate(Make("Shirt", "Shoes", props: new[] { ("size", "L") })).Should().BeFalse();
    }

    [Fact]
    public void MultipleTags_AllMustMatch()
    {
        var tags = new Dictionary<string, List<string>> { ["color"] = new() { "red" }, ["brand"] = new() { "Acme" } };
        var list = new ProductList<TestProduct>(tags: tags);
        var predicate = Compile(list);

        predicate(Make("W", "X", tags: new[] { ("color", "red"), ("brand", "Acme") })).Should().BeTrue();
        predicate(Make("W", "X", tags: new[] { ("color", "red") })).Should().BeFalse("both tag clauses AND together");
    }

    [Fact]
    public void SingleClause_StillWorks()
    {
        var list = new ProductList<TestProduct>(search: "Widget");
        var predicate = Compile(list);

        predicate(Make("Widget", "X")).Should().BeTrue();
        predicate(Make("Gadget", "X")).Should().BeFalse();
    }

    [Fact]
    public void NoClauses_ReturnsNull()
    {
        new ProductList<TestProduct>().Filter().Should().BeNull();
    }
}
