using System;
using System.Collections.Generic;
using System.Linq;
using Birko.Rules;
using FluentAssertions;
using Xunit;

namespace Birko.Rules.Tests;

public class RuleExpressionConverterTests
{
    // ── Test model ──

    private class Product
    {
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public int Stock { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? Category { get; set; }
        public Guid Id { get; set; }
        public int? Rating { get; set; }
        public Address? Address { get; set; }
    }

    private class Address
    {
        public string City { get; set; } = string.Empty;
    }

    private static readonly List<Product> Products =
    [
        new() { Name = "Widget", Price = 9.99m, Stock = 100, IsActive = true, CreatedAt = new DateTime(2025, 1, 1), Category = "Tools", Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), Rating = 5, Address = new Address { City = "Prague" } },
        new() { Name = "Gadget", Price = 24.99m, Stock = 0, IsActive = false, CreatedAt = new DateTime(2025, 6, 15), Category = "Electronics", Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), Rating = 3, Address = new Address { City = "Brno" } },
        new() { Name = "Gizmo", Price = 49.99m, Stock = 25, IsActive = true, CreatedAt = new DateTime(2026, 1, 10), Category = null, Id = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), Rating = null, Address = null },
    ];

    private List<Product> Apply(IRule rule)
    {
        var expr = RuleExpressionConverter.ToExpression<Product>(rule);
        expr.Should().NotBeNull();
        return Products.AsQueryable().Where(expr!).ToList();
    }

    // ── Equality ──

    [Fact]
    public void Equal_StringField_CaseInsensitive()
    {
        var result = Apply(new Rule("Name", ComparisonOperator.Equal, "widget"));
        result.Should().ContainSingle().Which.Name.Should().Be("Widget");
    }

    [Fact]
    public void NotEqual_ExcludesMatch()
    {
        var result = Apply(new Rule("Name", ComparisonOperator.NotEqual, "Widget"));
        result.Should().HaveCount(2);
        result.Should().NotContain(p => p.Name == "Widget");
    }

    // ── Numeric comparisons ──

    [Fact]
    public void GreaterThan_DecimalField()
    {
        var result = Apply(new Rule("Price", ComparisonOperator.GreaterThan, 20m));
        result.Should().HaveCount(2);
    }

    [Fact]
    public void LessThanOrEqual_IntField()
    {
        var result = Apply(new Rule("Stock", ComparisonOperator.LessThanOrEqual, 25));
        result.Should().HaveCount(2);
    }

    [Fact]
    public void GreaterThanOrEqual()
    {
        var result = Apply(new Rule("Price", ComparisonOperator.GreaterThanOrEqual, 24.99m));
        result.Should().HaveCount(2);
    }

    [Fact]
    public void LessThan()
    {
        var result = Apply(new Rule("Price", ComparisonOperator.LessThan, 10m));
        result.Should().ContainSingle().Which.Name.Should().Be("Widget");
    }

    // ── Between ──

    [Fact]
    public void Between_InclusiveRange()
    {
        var rule = Rule.Between("Price", 10m, 30m);
        var result = Apply(rule);
        result.Should().ContainSingle().Which.Name.Should().Be("Gadget");
    }

    // ── Null checks ──

    [Fact]
    public void IsNull_NullableReference()
    {
        var result = Apply(new Rule("Category", ComparisonOperator.IsNull, null));
        result.Should().ContainSingle().Which.Name.Should().Be("Gizmo");
    }

    [Fact]
    public void IsNotNull_NullableReference()
    {
        var result = Apply(new Rule("Category", ComparisonOperator.IsNotNull, null));
        result.Should().HaveCount(2);
    }

    [Fact]
    public void IsNull_NullableValueType()
    {
        var result = Apply(new Rule("Rating", ComparisonOperator.IsNull, null));
        result.Should().ContainSingle().Which.Name.Should().Be("Gizmo");
    }

    [Fact]
    public void IsNotNull_NullableValueType()
    {
        var result = Apply(new Rule("Rating", ComparisonOperator.IsNotNull, null));
        result.Should().HaveCount(2);
    }

    // ── String operators ──

    [Fact]
    public void Contains_Substring()
    {
        var result = Apply(new Rule("Name", ComparisonOperator.Contains, "adg"));
        result.Should().ContainSingle().Which.Name.Should().Be("Gadget");
    }

    [Fact]
    public void NotContains_Substring()
    {
        var result = Apply(new Rule("Name", ComparisonOperator.NotContains, "adg"));
        result.Should().HaveCount(2);
    }

    [Fact]
    public void StartsWith()
    {
        var result = Apply(new Rule("Name", ComparisonOperator.StartsWith, "G"));
        result.Should().HaveCount(2);
    }

    [Fact]
    public void EndsWith()
    {
        var result = Apply(new Rule("Name", ComparisonOperator.EndsWith, "mo"));
        result.Should().ContainSingle().Which.Name.Should().Be("Gizmo");
    }

    [Fact]
    public void Contains_NullProperty_NoException()
    {
        var result = Apply(new Rule("Category", ComparisonOperator.Contains, "Tool"));
        result.Should().ContainSingle().Which.Name.Should().Be("Widget");
    }

    // ── Like (SQL-style wildcards) ──

    [Fact]
    public void Like_BothWildcards()
    {
        var result = Apply(new Rule("Name", ComparisonOperator.Like, "%idg%"));
        result.Should().ContainSingle().Which.Name.Should().Be("Widget");
    }

    [Fact]
    public void Like_StartWildcard()
    {
        var result = Apply(new Rule("Name", ComparisonOperator.Like, "%mo"));
        result.Should().ContainSingle().Which.Name.Should().Be("Gizmo");
    }

    [Fact]
    public void Like_EndWildcard()
    {
        var result = Apply(new Rule("Name", ComparisonOperator.Like, "Wi%"));
        result.Should().ContainSingle().Which.Name.Should().Be("Widget");
    }

    [Fact]
    public void Like_NoWildcard_ExactMatch()
    {
        var result = Apply(new Rule("Name", ComparisonOperator.Like, "Widget"));
        result.Should().ContainSingle().Which.Name.Should().Be("Widget");
    }

    // ── In / NotIn ──

    [Fact]
    public void In_Collection()
    {
        var result = Apply(new Rule("Name", ComparisonOperator.In, new[] { "Widget", "Gizmo" }));
        result.Should().HaveCount(2);
    }

    [Fact]
    public void NotIn_Collection()
    {
        var result = Apply(new Rule("Name", ComparisonOperator.NotIn, new[] { "Widget", "Gizmo" }));
        result.Should().ContainSingle().Which.Name.Should().Be("Gadget");
    }

    [Fact]
    public void In_SingleValue()
    {
        var result = Apply(new Rule("Stock", ComparisonOperator.In, new[] { 0, 100 }));
        result.Should().HaveCount(2);
    }

    // ── Boolean field ──

    [Fact]
    public void Equal_BooleanField()
    {
        var result = Apply(new Rule("IsActive", ComparisonOperator.Equal, true));
        result.Should().HaveCount(2);
    }

    // ── DateTime field ──

    [Fact]
    public void GreaterThan_DateTime()
    {
        var result = Apply(new Rule("CreatedAt", ComparisonOperator.GreaterThan, new DateTime(2025, 12, 31)));
        result.Should().ContainSingle().Which.Name.Should().Be("Gizmo");
    }

    // ── Guid field ──

    [Fact]
    public void Equal_Guid_FromString()
    {
        var result = Apply(new Rule("Id", ComparisonOperator.Equal, "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        result.Should().ContainSingle().Which.Name.Should().Be("Widget");
    }

    // ── Negation ──

    [Fact]
    public void IsNegated_InvertsResult()
    {
        var rule = new Rule("IsActive", ComparisonOperator.Equal, true) { IsNegated = true };
        var result = Apply(rule);
        result.Should().ContainSingle().Which.Name.Should().Be("Gadget");
    }

    // ── Nested property ──

    [Fact]
    public void NestedProperty_Access()
    {
        var result = Apply(new Rule("Address.City", ComparisonOperator.Equal, "Prague"));
        result.Should().ContainSingle().Which.Name.Should().Be("Widget");
    }

    // ── Disabled rules ──

    [Fact]
    public void DisabledRule_ReturnsNull()
    {
        var rule = new Rule("Name", ComparisonOperator.Equal, "Widget") { IsEnabled = false };
        var expr = RuleExpressionConverter.ToExpression<Product>(rule);
        expr.Should().BeNull();
    }

    [Fact]
    public void UnknownProperty_ReturnsNull()
    {
        var rule = new Rule("NonExistent", ComparisonOperator.Equal, "x");
        var expr = RuleExpressionConverter.ToExpression<Product>(rule);
        expr.Should().BeNull();
    }

    // ── RuleGroup ──

    [Fact]
    public void AndGroup_AllMustMatch()
    {
        var group = RuleGroup.And(
            new Rule("IsActive", ComparisonOperator.Equal, true),
            new Rule("Price", ComparisonOperator.LessThan, 20m));

        var result = Apply(group);
        result.Should().ContainSingle().Which.Name.Should().Be("Widget");
    }

    [Fact]
    public void OrGroup_AnyCanMatch()
    {
        var group = RuleGroup.Or(
            new Rule("Stock", ComparisonOperator.Equal, 0),
            new Rule("Price", ComparisonOperator.GreaterThan, 40m));

        var result = Apply(group);
        result.Should().HaveCount(2);
        result.Select(p => p.Name).Should().BeEquivalentTo("Gadget", "Gizmo");
    }

    [Fact]
    public void NestedGroups()
    {
        // (IsActive AND Price < 50) OR Stock == 0
        var group = RuleGroup.Or(
            RuleGroup.And(
                new Rule("IsActive", ComparisonOperator.Equal, true),
                new Rule("Price", ComparisonOperator.LessThan, 50m)),
            new Rule("Stock", ComparisonOperator.Equal, 0));

        var result = Apply(group);
        result.Should().HaveCount(3); // All match
    }

    [Fact]
    public void NegatedGroup()
    {
        var group = RuleGroup.And(
            new Rule("IsActive", ComparisonOperator.Equal, true),
            new Rule("Price", ComparisonOperator.LessThan, 20m));
        group.IsNegated = true;

        var result = Apply(group);
        result.Should().HaveCount(2); // Gadget and Gizmo (NOT Widget)
    }

    [Fact]
    public void EmptyGroup_ReturnsNull()
    {
        var group = RuleGroup.And();
        var expr = RuleExpressionConverter.ToExpression<Product>(group);
        expr.Should().BeNull();
    }

    [Fact]
    public void Group_AllDisabled_ReturnsNull()
    {
        var group = RuleGroup.And(
            new Rule("Name", ComparisonOperator.Equal, "x") { IsEnabled = false },
            new Rule("Name", ComparisonOperator.Equal, "y") { IsEnabled = false });

        var expr = RuleExpressionConverter.ToExpression<Product>(group);
        expr.Should().BeNull();
    }

    // ── RuleSet ──

    [Fact]
    public void RuleSet_CombinesWithAnd()
    {
        var set = new RuleSet("test",
            new Rule("IsActive", ComparisonOperator.Equal, true),
            new Rule("Stock", ComparisonOperator.GreaterThan, 10));

        var expr = RuleExpressionConverter.ToExpression<Product>(set);
        expr.Should().NotBeNull();

        var result = Products.AsQueryable().Where(expr!).ToList();
        result.Should().HaveCount(2);
        result.Select(p => p.Name).Should().BeEquivalentTo("Widget", "Gizmo");
    }

    [Fact]
    public void RuleSet_Disabled_ReturnsNull()
    {
        var set = new RuleSet("test") { IsEnabled = false };
        set.Rules.Add(new Rule("Name", ComparisonOperator.Equal, "Widget"));

        var expr = RuleExpressionConverter.ToExpression<Product>(set);
        expr.Should().BeNull();
    }

    [Fact]
    public void RuleSet_SkipsDisabledRules()
    {
        var set = new RuleSet("test",
            new Rule("IsActive", ComparisonOperator.Equal, true),
            new Rule("Stock", ComparisonOperator.Equal, 999) { IsEnabled = false });

        var expr = RuleExpressionConverter.ToExpression<Product>(set);
        expr.Should().NotBeNull();

        var result = Products.AsQueryable().Where(expr!).ToList();
        result.Should().HaveCount(2); // Stock=999 rule skipped
    }

    // ── IEnumerable<IRule> overload ──

    [Fact]
    public void MultipleRules_CombinedWithAnd()
    {
        IEnumerable<IRule> rules =
        [
            new Rule("Price", ComparisonOperator.GreaterThan, 5m),
            new Rule("Price", ComparisonOperator.LessThan, 30m)
        ];

        var expr = RuleExpressionConverter.ToExpression<Product>(rules);
        expr.Should().NotBeNull();

        var result = Products.AsQueryable().Where(expr!).ToList();
        result.Should().HaveCount(2);
        result.Select(p => p.Name).Should().BeEquivalentTo("Widget", "Gadget");
    }

    // ── Value type conversion ──

    [Fact]
    public void IntValue_ConvertedToDecimal()
    {
        // Pass int 10 for decimal Price field
        var result = Apply(new Rule("Price", ComparisonOperator.GreaterThan, 10));
        result.Should().HaveCount(2);
    }

    [Fact]
    public void StringDateTime_Converted()
    {
        var result = Apply(new Rule("CreatedAt", ComparisonOperator.GreaterThan, "2025-12-31"));
        result.Should().ContainSingle().Which.Name.Should().Be("Gizmo");
    }

    // ── Case-insensitive property names ──

    [Fact]
    public void PropertyName_CaseInsensitive()
    {
        var result = Apply(new Rule("name", ComparisonOperator.Equal, "Widget"));
        result.Should().ContainSingle().Which.Name.Should().Be("Widget");
    }
}
