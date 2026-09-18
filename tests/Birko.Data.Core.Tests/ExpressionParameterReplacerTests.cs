using System;
using System.Linq.Expressions;
using Birko.Data.Expressions;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Core.Tests;

public class ExpressionParameterReplacerTests
{
    private sealed class Item
    {
        public int Value { get; set; }
        public string? Name { get; set; }
    }

    [Fact]
    public void AndAlso_combines_two_predicates_with_a_shared_parameter()
    {
        Expression<Func<Item, bool>> left = x => x.Value > 5;
        Expression<Func<Item, bool>> right = x => x.Name == "ok";

        var combined = ExpressionParameterReplacer.AndAlso(left, right).Compile();

        combined(new Item { Value = 10, Name = "ok" }).Should().BeTrue();
        combined(new Item { Value = 10, Name = "no" }).Should().BeFalse();
        combined(new Item { Value = 1, Name = "ok" }).Should().BeFalse();
    }

    [Fact]
    public void AndAlso_with_null_left_returns_right_unchanged()
    {
        Expression<Func<Item, bool>> right = x => x.Value > 5;

        var result = ExpressionParameterReplacer.AndAlso(null, right);

        result.Should().BeSameAs(right);
    }

    [Fact]
    public void OrElse_combines_two_predicates_with_a_shared_parameter()
    {
        Expression<Func<Item, bool>> left = x => x.Value > 100;
        Expression<Func<Item, bool>> right = x => x.Name == "ok";

        var combined = ExpressionParameterReplacer.OrElse(left, right).Compile();

        combined(new Item { Value = 1, Name = "ok" }).Should().BeTrue();
        combined(new Item { Value = 200, Name = "no" }).Should().BeTrue();
        combined(new Item { Value = 1, Name = "no" }).Should().BeFalse();
    }

    [Fact]
    public void OrElse_with_null_left_returns_right_unchanged()
    {
        Expression<Func<Item, bool>> right = x => x.Name == "ok";

        var result = ExpressionParameterReplacer.OrElse(null, right);

        result.Should().BeSameAs(right);
    }

    [Fact]
    public void Combined_expression_uses_a_single_parameter_instance()
    {
        // The whole point of the replacer: no InvocationExpression nodes, one shared parameter,
        // so Birko's SQL parser can walk the tree.
        Expression<Func<Item, bool>> left = x => x.Value > 5;
        Expression<Func<Item, bool>> right = x => x.Value < 50;

        var combined = ExpressionParameterReplacer.AndAlso(left, right);

        combined.Parameters.Should().HaveCount(1);
        combined.Body.NodeType.Should().Be(ExpressionType.AndAlso);
    }
}
