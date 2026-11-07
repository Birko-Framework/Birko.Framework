using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.ElasticSearch.Stores;
using Birko.Data.Models;
using FluentAssertions;
using Nest;
using Xunit;
using Xunit.Abstractions;
using ES = Birko.Data.ElasticSearch.ElasticSearch;

namespace Birko.Data.ElasticSearch.Tests;

public sealed class Task835Doc : AbstractModel
{
    public string? Name { get; set; }
}

/// <summary>
/// TASK-835: Elasticsearch refuses a terms query holding more than <c>index.max_terms_count</c> terms (65,536 by
/// default), so <c>ids.Contains(x.Guid)</c> with a longer list failed the read. A longer list is now a bool
/// <c>should</c> of terms queries, each within <see cref="ElasticSearch.MaxTermsCount"/>: still one request, so it
/// stays correct under <c>||</c> and <c>!</c>.
/// </summary>
public sealed class Task835TermsCountLimitTests : IDisposable
{
    private readonly ElasticLive _live;

    public Task835TermsCountLimitTests(ITestOutputHelper output) => _live = new ElasticLive(output);

    public void Dispose() => _live.Dispose();

    private static List<Guid> Ids(int count) => Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToList();

    [Fact]
    public void The_declared_limit_is_Elasticsearchs_default_max_terms_count()
    {
        ES.MaxTermsCount.Should().Be(65_536);
    }

    [Fact]
    public void A_list_within_the_limit_is_one_terms_query()
    {
        var ids = Ids(ES.MaxTermsCount);

        var query = ES.ParseRequiredFilterQuery<Task835Doc>(x => ids.Contains(x.Guid!.Value));

        var terms = query.Should().BeOfType<TermsQuery>().Subject;
        terms.Terms.Cast<object>().Should().HaveCount(ES.MaxTermsCount);
    }

    [Theory]
    [InlineData(65_537, 2)]
    [InlineData(131_072, 2)]
    [InlineData(131_073, 3)]
    public void A_list_past_the_limit_is_a_should_of_terms_queries_each_within_it(int count, int chunks)
    {
        var ids = Ids(count);

        var query = ES.ParseRequiredFilterQuery<Task835Doc>(x => ids.Contains(x.Guid!.Value));

        var parts = ShouldOf(query);
        parts.Should().HaveCount(chunks);
        parts.Should().OnlyContain(t => t.Terms.Cast<object>().Count() <= ES.MaxTermsCount);
        parts.Select(t => t.Field.Name).Distinct().Should().ContainSingle();
        parts.SelectMany(t => t.Terms.Cast<Guid>()).Should().Equal(ids);
    }

    [Fact]
    public void A_negated_list_past_the_limit_is_must_not_of_the_should()
    {
        var ids = Ids(ES.MaxTermsCount + 1);

        var query = ES.ParseRequiredFilterQuery<Task835Doc>(x => !ids.Contains(x.Guid!.Value));

        var outer = query.Should().BeOfType<BoolQuery>().Subject;
        outer.Must.Should().BeNullOrEmpty();
        outer.Should.Should().BeNullOrEmpty();
        var negated = outer.MustNot.Should().ContainSingle().Subject;
        ShouldOf(Unwrap(negated)).SelectMany(t => t.Terms.Cast<Guid>()).Should().Equal(ids);
    }

    [Fact]
    public void The_split_keeps_every_term_and_the_order_at_a_small_limit()
    {
        var terms = Enumerable.Range(0, 10).Cast<object>().ToArray();

        var parts = ShouldOf(ES.TermsWithin("f", terms, 3));

        parts.Select(p => p.Terms.Cast<object>().Count()).Should().Equal(3, 3, 3, 1);
        parts.SelectMany(p => p.Terms.Cast<object>()).Should().Equal(terms);
        ES.TermsWithin("f", terms, 10).Should().BeOfType<TermsQuery>();
    }

    [Fact]
    public async Task A_read_past_the_limit_equals_the_oracle_and_so_does_its_negation()
    {
        if (!_live.Ready()) return;
        var store = new AsyncElasticSearchStore<Task835Doc>();
        store.SetSettings(_live.NewSettings());
        var stored = new List<Guid>();
        for (var i = 0; i < 20; i++)
            stored.Add(await store.CreateAsync(new Task835Doc { Name = "d" + i }));
        ElasticLive.Refresh();

        var wanted = stored.Where((_, i) => i % 2 == 0).ToList();
        var ids = Ids(ES.MaxTermsCount + 1 - wanted.Count).Concat(wanted).ToList();
        ids.Should().HaveCount(65_537);

        var hits = await store.ReadAsync(x => ids.Contains(x.Guid!.Value));
        hits.Select(h => h.Guid!.Value).Should().BeEquivalentTo(wanted);

        var misses = await store.ReadAsync(x => !ids.Contains(x.Guid!.Value));
        misses.Select(h => h.Guid!.Value).Should().BeEquivalentTo(stored.Except(wanted));

        (await store.CountAsync(x => ids.Contains(x.Guid!.Value) || x.Name == "d1")).Should().Be(wanted.Count + 1);
    }

    private static IReadOnlyList<TermsQuery> ShouldOf(QueryBase query)
    {
        var should = query.Should().BeOfType<BoolQuery>().Subject;
        should.MinimumShouldMatch.Should().NotBeNull();
        should.MinimumShouldMatch!.Match(i => i, _ => null).Should().Be(1);
        should.Must.Should().BeNullOrEmpty();
        should.MustNot.Should().BeNullOrEmpty();
        should.Filter.Should().BeNullOrEmpty();
        return should.Should.Select(c => Unwrap(c).Should().BeOfType<TermsQuery>().Subject).ToList();
    }

    private static QueryBase Unwrap(QueryContainer container)
    {
        IQueryContainer c = container;
        return (QueryBase?)c.Bool ?? (QueryBase?)c.Terms
            ?? throw new InvalidOperationException("Expected a bool or a terms query.");
    }
}
