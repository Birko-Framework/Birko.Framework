using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Birko.Configuration;
using Birko.Data.ElasticSearch.Stores;
using Birko.Data.ElasticSearch.Tests.TestResources.Models;
using Birko.Data.Models;
using FluentAssertions;
using Nest;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.ElasticSearch.Tests;

/// <summary>
/// TASK-153, the ElasticSearch half of the medium <c>filter-expression-translation</c> findings. The rendered query is
/// asserted where the defect was the shape of the query; the live class below asserts the documents where it was an
/// answer the index gave.
/// </summary>
public class FilterTranslationTriageTests
{
    public class SubModel : AbstractModel
    {
        public DateModel Sub { get; set; } = new();
        public int Other { get; set; }
    }

    private static QueryBase? Parse(Expression<Func<DateModel, bool>> f) => Birko.Data.ElasticSearch.ElasticSearch.ParseExpression(f);

    /// <summary>SH-M124. `18 > x.Count` is `x.Count < 18`; it rendered `count > 18`.</summary>
    [Fact]
    public void A_value_on_the_left_of_a_comparison_mirrors_the_operator()
    {
        var range = Parse(x => 18 > x.Count).Should().BeOfType<NumericRangeQuery>().Subject;
        range.LessThan.Should().Be(18);
        range.GreaterThan.Should().BeNull();

        var range2 = Parse(x => 18 <= x.Count).Should().BeOfType<NumericRangeQuery>().Subject;
        range2.GreaterThanOrEqualTo.Should().Be(18);
        range2.LessThanOrEqualTo.Should().BeNull();
    }

    /// <summary>SH-M126. Two fields compared rendered "the first is missing"; a term query cannot compare two fields.</summary>
    [Fact]
    public void Two_fields_compared_are_refused_not_read_as_a_missing_field()
    {
        var act = () => Birko.Data.ElasticSearch.ElasticSearch.ParseFilterQuery<DateModel>(x => x.Count == x.Count);
        act.Should().Throw<NotSupportedException>();
    }

    /// <summary>SH-M141. Null-or-empty is "missing, or exactly empty"; `MustNot wildcard *` excluded an indexed "".</summary>
    [Fact]
    public void IsNullOrEmpty_is_missing_or_exactly_empty()
    {
        var query = Parse(x => string.IsNullOrEmpty(x.Text)).Should().BeOfType<BoolQuery>().Subject;
        query.Should.Should().HaveCount(2);
        query.MinimumShouldMatch.Should().NotBeNull();
        query.MustNot.Should().BeNullOrEmpty();
    }

    /// <summary>SH-M142. `x.Sub.Count == 1` ran as `sub == 1`, the parent's field.</summary>
    [Fact]
    public void A_member_of_a_member_is_refused_not_read_as_its_parent()
    {
        var act = () => Birko.Data.ElasticSearch.ElasticSearch.ParseFilterQuery<SubModel>(x => x.Sub.Count == 1);
        act.Should().Throw<NotSupportedException>();
    }

    /// <summary>SH-M143. A parameterless `Any()` wrapped a term query with no value; any nested document matches.</summary>
    [Fact]
    public void Any_without_a_predicate_matches_any_nested_document()
    {
        Expression<Func<NestedDateModel, bool>> anyNested = x => x.Nested.Any();
        var nested = Birko.Data.ElasticSearch.ElasticSearch.ParseExpression(anyNested)
            .Should().BeOfType<NestedQuery>().Subject;
        ((IQueryContainer)nested.Query).MatchAll.Should().NotBeNull();
    }

    /// <summary>
    /// SH-M151. Two closures from one call site print the same text, and the compiled-value cache was keyed by it,
    /// so the second evaluation returned the first closure's value.
    /// </summary>
    [Fact]
    public void Two_closures_from_one_call_site_each_bind_their_own_value()
    {
        static Expression<Func<DateModel, bool>> Over(int limit) => x => x.Count > limit * 2;

        var first = Parse(Over(5)).Should().BeOfType<NumericRangeQuery>().Subject;
        var second = Parse(Over(50)).Should().BeOfType<NumericRangeQuery>().Subject;

        first.GreaterThan.Should().Be(10);
        second.GreaterThan.Should().Be(100);
    }

    private sealed class OtherSettings : ISettings
    {
        public string GetId() => "other";
        public void LoadFrom(ISettings data) { }
    }

    /// <summary>SH-M146. Foreign settings were ignored, leaving a store whose writes reported success and whose reads were empty.</summary>
    [Fact]
    public void A_store_refuses_settings_it_cannot_use()
    {
        var act = () => new ElasticSearchStore<DateModel>().SetSettings(new OtherSettings());
        act.Should().Throw<ArgumentException>();
        var actAsync = () => new AsyncElasticSearchStore<DateModel>().SetSettings(new OtherSettings());
        actAsync.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task An_unconfigured_store_refuses_a_filtered_delete_instead_of_reporting_success()
    {
        var act = () => new ElasticSearchStore<DateModel>().Delete(x => x.Count == 1);
        act.Should().Throw<InvalidOperationException>();
        var actAsync = () => new AsyncElasticSearchStore<DateModel>().DeleteAsync(x => x.Count == 1);
        await actAsync.Should().ThrowAsync<InvalidOperationException>();
    }
}

public class TextDoc : AbstractModel
{
    public string? Text { get; set; }
    public int Count { get; set; }
}

/// <summary>TASK-153 against a live index: the documents each fixed translation selects.</summary>
public sealed class FilterTranslationTriageLiveTests : IDisposable
{
    private readonly ElasticLive _live;
    public FilterTranslationTriageLiveTests(ITestOutputHelper output) => _live = new ElasticLive(output);
    public void Dispose() => _live.Dispose();

    private async Task<AsyncElasticSearchStore<TextDoc>> Seeded()
    {
        var store = new AsyncElasticSearchStore<TextDoc>();
        store.SetSettings(_live.NewSettings());
        await store.CreateAsync(new TextDoc { Text = "", Count = 1 });
        await store.CreateAsync(new TextDoc { Text = null, Count = 2 });
        await store.CreateAsync(new TextDoc { Text = "a", Count = 30 });
        ElasticLive.Refresh();
        return store;
    }

    private static async Task<int[]> Counts(AsyncElasticSearchStore<TextDoc> store, Expression<Func<TextDoc, bool>> filter)
        => (await store.ReadAsync(filter)).Select(d => d.Count).OrderBy(c => c).ToArray();

    [Fact]
    public async Task IsNullOrEmpty_finds_an_indexed_empty_string()
    {
        if (!_live.Ready()) return;
        var store = await Seeded();
        (await Counts(store, x => string.IsNullOrEmpty(x.Text))).Should().Equal(1, 2);
        (await Counts(store, x => !string.IsNullOrEmpty(x.Text))).Should().Equal(30);
    }

    [Fact]
    public async Task A_value_on_the_left_selects_what_CSharp_selects()
    {
        if (!_live.Ready()) return;
        var store = await Seeded();
        (await Counts(store, x => 10 > x.Count)).Should().Equal(1, 2);
    }
}
