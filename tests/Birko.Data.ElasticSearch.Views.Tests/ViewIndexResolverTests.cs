using System;
using System.Collections.Generic;
using Birko.Data.ElasticSearch.Views;
using Birko.Data.Views;
using FluentAssertions;
using Xunit;

namespace Birko.Data.ElasticSearch.Views.Tests;

/// <summary>
/// CR-M091: the store resolved the index name using definition.Name only in Persistent mode, while
/// the manager used definition.Name whenever present — so for a named Auto/OnTheFly view the manager
/// ensured/checked one index while the store queried another. Both now delegate to the shared
/// ElasticSearchViewIndexResolver, so these assertions pin the single agreed rule.
/// </summary>
public class ViewIndexResolverTests
{
    private class ReviewSource { }

    private static ViewDefinition Define(string? name, ViewQueryMode mode) => new(
        name: name!,
        queryMode: mode,
        primarySource: typeof(ReviewSource),
        viewType: typeof(ReviewSource),
        fields: new List<FieldSelector>(),
        joins: new List<JoinClause>(),
        aggregates: new List<AggregateClause>(),
        groupBy: new List<GroupByClause>(),
        hints: new Dictionary<string, object>());

    [Fact]
    public void Persistent_with_name_uses_the_view_name()
    {
        ElasticSearchViewIndexResolver.Resolve(Define("MyView", ViewQueryMode.Persistent))
            .Should().Be("myview");
    }

    [Fact]
    public void Persistent_without_name_falls_back_to_the_source()
    {
        ElasticSearchViewIndexResolver.Resolve(Define(null, ViewQueryMode.Persistent))
            .Should().Be("reviewsource");
    }

    [Theory]
    [InlineData(ViewQueryMode.OnTheFly)]
    [InlineData(ViewQueryMode.Auto)]
    public void NonPersistent_with_name_still_uses_the_source_index(ViewQueryMode mode)
    {
        // The fix: a name present on a non-Persistent view must NOT change the resolved index — the
        // view is computed over the source's own index, and the manager must agree with the store.
        ElasticSearchViewIndexResolver.Resolve(Define("MyView", mode))
            .Should().Be("reviewsource");
    }
}
