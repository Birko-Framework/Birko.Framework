using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using Birko.Data.Views;
using FluentAssertions;
using Nest;
using Xunit;

namespace Birko.Data.ElasticSearch.Views.Tests;

/// <summary>
/// TASK-851: a view store splits a filter's collection <c>Contains</c> at its own <c>MaxTermsCount</c>, not at the
/// process-wide default, and two view stores with different limits do not see each other's.
/// </summary>
public class Task851ViewTermsLimitTests
{
    private class TaggedView
    {
        public Guid Id { get; set; }
    }

    private static ViewDefinition Definition() => new(
        name: "v",
        queryMode: ViewQueryMode.OnTheFly,
        primarySource: typeof(TaggedView),
        viewType: typeof(TaggedView),
        fields: new List<FieldSelector>(),
        joins: new List<JoinClause>(),
        aggregates: new List<AggregateClause>(),
        groupBy: new List<GroupByClause>(),
        hints: new Dictionary<string, object>());

    private static readonly MethodInfo Build = typeof(ElasticSearchViewStore<TaggedView>)
        .GetMethod("BuildFilterQuery", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static List<int> Chunks(ElasticSearchViewStore<TaggedView> store, Expression<Func<TaggedView, bool>> filter)
    {
        IQueryContainer query = (QueryContainer)Build.Invoke(store, new object?[] { filter })!;
        if (query.Terms != null)
            return new List<int> { query.Terms.Terms.Count() };
        return query.Bool!.Should!.Select(c => ((IQueryContainer)c).Terms!.Terms.Count()).ToList();
    }

    [Fact]
    public async Task Two_view_stores_split_at_their_own_limit_in_parallel()
    {
        var two = new ElasticSearchViewStore<TaggedView>(new ElasticClient(), Definition()) { MaxTermsCount = 2 };
        var five = new ElasticSearchViewStore<TaggedView>(new ElasticClient(), Definition()) { MaxTermsCount = 5 };
        var ids = Enumerable.Range(0, 11).Select(_ => Guid.NewGuid()).ToList();

        var results = await Task.WhenAll(Enumerable.Range(0, 100).Select(i => Task.Run(() =>
            (Even: i % 2 == 0, Chunks: Chunks(i % 2 == 0 ? two : five, v => ids.Contains(v.Id))))));

        results.Where(r => r.Even).Should().OnlyContain(r => r.Chunks.SequenceEqual(new[] { 2, 2, 2, 2, 2, 1 }));
        results.Where(r => !r.Even).Should().OnlyContain(r => r.Chunks.SequenceEqual(new[] { 5, 5, 1 }));
    }

    [Fact]
    public void A_view_store_without_a_limit_uses_the_default()
    {
        var store = new ElasticSearchViewStore<TaggedView>(new ElasticClient(), Definition());
        var ids = Enumerable.Range(0, 11).Select(_ => Guid.NewGuid()).ToList();

        store.MaxTermsCount.Should().BeNull();
        Chunks(store, v => ids.Contains(v.Id)).Should().Equal(11);
    }

    [Fact]
    public void A_limit_below_one_is_refused()
    {
        var act = () => new ElasticSearchViewStore<TaggedView>(new ElasticClient(), Definition()) { MaxTermsCount = 0 };

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
