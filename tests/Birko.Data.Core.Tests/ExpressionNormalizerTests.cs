using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Birko.Data.Expressions;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Core.Tests;

public class ExpressionNormalizerTests
{
    public sealed class Item
    {
        public int Amount { get; set; }
        public bool Active { get; set; }
        public bool? Flag { get; set; }
        public int? Score { get; set; }
        public string? Name { get; set; }
    }

    private static readonly Item[] Sample =
    {
        new Item { Amount = 1, Active = true,  Flag = true,  Score = 10,   Name = "alpha" },
        new Item { Amount = 5, Active = false, Flag = null,  Score = null, Name = "beta" },
        new Item { Amount = 9, Active = true,  Flag = false, Score = 30,   Name = "gamma" },
    };

    private static bool Contains(Expression expr, params ExpressionType[] nodeTypes)
    {
        var finder = new NodeTypeFinder(nodeTypes);
        finder.Visit(expr);
        return finder.Found;
    }

    private sealed class NodeTypeFinder : ExpressionVisitor
    {
        private readonly HashSet<ExpressionType> _types;
        public bool Found { get; private set; }
        public NodeTypeFinder(ExpressionType[] types) => _types = new HashSet<ExpressionType>(types);
        public override Expression? Visit(Expression? node)
        {
            if (node != null && _types.Contains(node.NodeType)) Found = true;
            return Found ? node : base.Visit(node);
        }
    }

    // Recompiles the normalized body against the original parameter and asserts it agrees with the
    // original delegate on every sample — the core guarantee: normalization preserves semantics.
    private static void AssertSemanticParity(Expression<Func<Item, bool>> original)
    {
        var normalizedBody = ExpressionNormalizer.Normalize(original.Body)!;
        var normalized = Expression.Lambda<Func<Item, bool>>(normalizedBody, original.Parameters).Compile();
        var reference = original.Compile();
        foreach (var item in Sample)
            normalized(item).Should().Be(reference(item), $"item Amount={item.Amount}");
    }

    [Theory]
    [MemberData(nameof(SemanticCases))]
    public void Normalize_preserves_semantics(string label, Expression<Func<Item, bool>> expr)
    {
        _ = label;
        AssertSemanticParity(expr);
    }

    public static IEnumerable<object[]> SemanticCases()
    {
        var draft = false;
        var cases = new (string, Expression<Func<Item, bool>>)[]
        {
            ("ternary",           x => x.Amount > 4 ? x.Active : x.Score == null),
            ("ternaryConstFold",  x => (draft ? x.Active : x.Amount > 4)),
            ("coalesceBool",      x => x.Flag ?? false),
            ("coalesceBoolTrue",  x => x.Flag ?? true),
            ("nestedTernary",     x => (x.Amount > 4 ? x.Active : x.Score == null) && x.Name!.StartsWith("g")),
            ("plainComparison",   x => x.Amount >= 5),
            ("boolAlgebra",       x => x.Active && (x.Amount > 4 || x.Score == null)),
        };
        foreach (var c in cases)
            yield return new object[] { c.Item1, c.Item2 };
    }

    [Fact]
    public void Boolean_ternary_is_desugared_to_boolean_algebra()
    {
        Expression<Func<Item, bool>> expr = x => x.Amount > 4 ? x.Active : x.Score == null;
        var normalized = ExpressionNormalizer.Normalize(expr.Body)!;

        Contains(normalized, ExpressionType.Conditional).Should().BeFalse("boolean ternary must be lowered to AND/OR");
        Contains(normalized, ExpressionType.OrElse, ExpressionType.AndAlso).Should().BeTrue();
    }

    [Fact]
    public void Boolean_coalesce_is_desugared_away()
    {
        Expression<Func<Item, bool>> expr = x => x.Flag ?? false;
        var normalized = ExpressionNormalizer.Normalize(expr.Body)!;

        Contains(normalized, ExpressionType.Coalesce).Should().BeFalse("boolean ?? must be lowered to == true / == null");
    }

    [Fact]
    public void Parameter_free_subtree_is_funcletized_to_a_constant()
    {
        // (2 * 3 > 5) references no parameter → folds to a single ConstantExpression(true).
        Expression<Func<Item, bool>> expr = x => 2 * 3 > 5;
        var normalized = ExpressionNormalizer.Normalize(expr.Body)!;

        normalized.Should().BeOfType<ConstantExpression>();
        ((ConstantExpression)normalized).Value.Should().Be(true);
    }

    [Fact]
    public void Value_position_coalesce_is_preserved_for_the_value_parser()
    {
        // x.Score ?? 0 is a numeric (non-boolean) value → kept as Coalesce so the value parser renders COALESCE(...).
        Expression<Func<Item, int>> expr = x => x.Score ?? 0;
        var normalized = ExpressionNormalizer.Normalize(expr.Body)!;

        normalized.NodeType.Should().Be(ExpressionType.Coalesce);
    }

    [Fact]
    public void Value_position_ternary_is_preserved_for_the_value_parser()
    {
        Expression<Func<Item, int>> expr = x => x.Amount > 4 ? x.Amount : 0;
        var normalized = ExpressionNormalizer.Normalize(expr.Body)!;

        normalized.NodeType.Should().Be(ExpressionType.Conditional);
    }
}
