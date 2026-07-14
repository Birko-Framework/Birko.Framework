using System;
using System.Linq;
using System.Linq.Expressions;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Conditions;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.Tests.Expressions;

[Table("Tracks")]
public class TrackEntity : Birko.Data.Models.AbstractModel
{
    public string Title { get; set; } = string.Empty;
}

/// <summary>
/// Regression for the culture-aware string-pattern overloads. The translator used to feed EVERY
/// method argument into the condition value, so the trailing <see cref="StringComparison"/> (an enum,
/// e.g. OrdinalIgnoreCase == 5) overwrote the search string and produced <c>Title LIKE '%5%'</c>.
/// Only the first argument is the pattern; the comparison/culture args must be ignored.
/// </summary>
public class StringContainsComparisonTests
{
    private static Condition? FindLeaf(System.Collections.Generic.IEnumerable<Condition> conditions, string name)
    {
        foreach (var c in conditions)
        {
            if (c.Name != null && (string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)
                || c.Name.EndsWith("." + name, StringComparison.OrdinalIgnoreCase)))
                return c;
            if (c.SubConditions != null)
            {
                var found = FindLeaf(c.SubConditions, name);
                if (found != null) return found;
            }
        }
        return null;
    }

    [Fact]
    public void Contains_WithStringComparison_ProducesLikeOnThePattern_NotTheEnum()
    {
        Expression<Func<TrackEntity, bool>> expr =
            t => t.Title.Contains("kick", StringComparison.OrdinalIgnoreCase);

        var conditions = Birko.Data.SQL.DataBase.ParseConditionExpression(expr).ToList();

        var leaf = FindLeaf(conditions, "Title");
        leaf.Should().NotBeNull();
        leaf!.Type.Should().Be(ConditionType.Like);
        leaf.Values.Should().NotBeNull();
        var values = leaf.Values!.Cast<object>().ToList();
        values.Should().ContainSingle().Which.Should().Be("kick");
        // The StringComparison enum must not have leaked into the value.
        values.Should().NotContain(v => v is StringComparison);
    }

    [Fact]
    public void Contains_SingleArgOverload_StillWorks()
    {
        Expression<Func<TrackEntity, bool>> expr = t => t.Title.Contains("kick");

        var conditions = Birko.Data.SQL.DataBase.ParseConditionExpression(expr).ToList();

        var leaf = FindLeaf(conditions, "Title");
        leaf.Should().NotBeNull();
        leaf!.Type.Should().Be(ConditionType.Like);
        leaf.Values!.Cast<object>().Should().ContainSingle().Which.Should().Be("kick");
    }

    [Fact]
    public void StartsWith_WithStringComparison_UsesOnlyThePattern()
    {
        Expression<Func<TrackEntity, bool>> expr =
            t => t.Title.StartsWith("intro", StringComparison.OrdinalIgnoreCase);

        var conditions = Birko.Data.SQL.DataBase.ParseConditionExpression(expr).ToList();

        var leaf = FindLeaf(conditions, "Title");
        leaf.Should().NotBeNull();
        leaf!.Type.Should().Be(ConditionType.StartsWith);
        leaf.Values!.Cast<object>().Should().ContainSingle().Which.Should().Be("intro");
    }

    [Fact]
    public void EndsWith_WithStringComparison_UsesOnlyThePattern()
    {
        Expression<Func<TrackEntity, bool>> expr =
            t => t.Title.EndsWith("outro", StringComparison.OrdinalIgnoreCase);

        var conditions = Birko.Data.SQL.DataBase.ParseConditionExpression(expr).ToList();

        var leaf = FindLeaf(conditions, "Title");
        leaf.Should().NotBeNull();
        leaf!.Type.Should().Be(ConditionType.EndsWith);
        leaf.Values!.Cast<object>().Should().ContainSingle().Which.Should().Be("outro");
    }
}
