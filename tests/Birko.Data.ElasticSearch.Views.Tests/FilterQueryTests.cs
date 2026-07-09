using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using Birko.Data.ElasticSearch.Views;
using Birko.Data.Views;
using FluentAssertions;
using Nest;
using Xunit;

namespace Birko.Data.ElasticSearch.Views.Tests;

/// <summary>
/// Regression for CR-H047: a supplied filter that the parser can't translate (ParseExpression
/// returns null, or throws) used to fall back to MatchAll — silently returning the whole dataset
/// (a landmine for count / existence / permission checks). It now throws NotSupportedException;
/// only a null filter maps to MatchAll.
/// </summary>
public class FilterQueryTests
{
    private class ReviewView
    {
        public string? Category { get; set; }
    }

    private static ViewDefinition MinimalDefinition() => new(
        name: "v",
        queryMode: ViewQueryMode.OnTheFly,
        primarySource: typeof(ReviewView),
        viewType: typeof(ReviewView),
        fields: new List<FieldSelector>(),
        joins: new List<JoinClause>(),
        aggregates: new List<AggregateClause>(),
        groupBy: new List<GroupByClause>(),
        hints: new Dictionary<string, object>());

    private static object? BuildFilter(Expression<Func<ReviewView, bool>>? filter)
    {
        var store = new ElasticSearchViewStore<ReviewView>(new ElasticClient(), MinimalDefinition());
        var method = typeof(ElasticSearchViewStore<ReviewView>)
            .GetMethod("BuildFilterQuery", BindingFlags.NonPublic | BindingFlags.Instance)!;
        try
        {
            return method.Invoke(store, new object?[] { filter });
        }
        catch (TargetInvocationException tie)
        {
            throw tie.InnerException!;
        }
    }

    [Fact]
    public void NullFilter_YieldsMatchAll()
    {
        var query = BuildFilter(null);

        query.Should().NotBeNull();
        query.Should().BeAssignableTo<QueryContainer>();
    }

    [Fact]
    public void SupportedFilter_Translates()
    {
        var act = () => BuildFilter(v => v.Category == "books");

        act.Should().NotThrow("a simple binary comparison is translatable");
    }

    [Fact]
    public void UntranslatableFilter_Throws_DoesNotWidenToMatchAll()
    {
        // A conditional (ternary) body is not a supported node type — ParseExpression returns null.
        var act = () => BuildFilter(v => v.Category == "a" ? true : false);

        act.Should().Throw<NotSupportedException>("CR-H047: an untranslatable filter must not silently match everything");
    }
}
