using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.ElasticSearch.Stores;
using Birko.Data.ElasticSearch.Tests.TestResources.Models;
using Birko.Data.Localization.Decorators;
using Birko.Data.Localization.Models;
using Birko.Data.Stores;
using FluentAssertions;
using Nest;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.ElasticSearch.Tests;

public sealed class Task703Product : Birko.Data.Models.AbstractModel, ILocalizable
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;

    public IReadOnlyList<string> GetLocalizableFields() => new[] { nameof(Name) };
}

/// <summary>
/// TASK-703: <c>x.Nullable.Value</c> on a member of the entity is that member's field. The localized filters render
/// <c>x.Guid != null &amp;&amp; guids.Contains(x.Guid.Value)</c>, which the translator refused, so every filter on a
/// translated field threw on ElasticSearch (found by TASK-701's broad-match contract).
/// </summary>
public sealed class Task703NullableValueTranslationTests : IDisposable
{
    private readonly ElasticLive _live;

    public Task703NullableValueTranslationTests(ITestOutputHelper output) => _live = new ElasticLive(output);

    [Fact]
    public void A_list_contains_of_a_nullable_members_value_is_a_terms_query_on_that_field()
    {
        var guids = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };

        var query = Birko.Data.ElasticSearch.ElasticSearch.ParseRequiredFilterQuery<DateModel>(x => x.Guid != null && guids.Contains(x.Guid.Value));
        Expression<Func<DateModel, bool>> direct = x => guids.Cast<Guid?>().ToList().Contains(x.Guid);
        var expected = Birko.Data.ElasticSearch.ElasticSearch.ParseRequiredFilterQuery(direct);

        var terms = Flatten(query).OfType<TermsQuery>().Single();
        terms.Field.Name.Should().Be(Flatten(expected).OfType<TermsQuery>().Single().Field.Name);
        terms.Terms.Cast<object>().Should().BeEquivalentTo(guids.Cast<object>());
        Flatten(query).OfType<ExistsQuery>().Single().Field.Name.Should().Be(terms.Field.Name);
    }

    [Fact]
    public void An_equality_on_a_nullable_members_value_is_a_term_on_that_field()
    {
        var guid = Guid.NewGuid();

        var query = Birko.Data.ElasticSearch.ElasticSearch.ParseRequiredFilterQuery<DateModel>(x => x.Guid!.Value == guid);

        var term = Flatten(query).OfType<TermQuery>().Single();
        term.Field.Name.Should().Be("guid");
        term.Value.Should().Be(guid);
    }

    [Fact]
    public void A_filter_on_a_translated_field_runs_on_the_store()
    {
        if (!_live.Ready()) return;
        var settings = _live.NewSettings();
        var products = new AsyncElasticSearchStore<Task703Product>();
        products.SetSettings(settings);
        var translations = new AsyncElasticSearchStore<EntityTranslationModel>();
        translations.SetSettings(settings);
        var bulk = new AsyncLocalizedBulkStoreWrapper<IAsyncBulkStore<Task703Product>, Task703Product>(products, translations, new Slovak());
        var stolicka = new Task703Product { Name = "stolicka", Code = "c" };
        bulk.CreateAsync(new[] { stolicka, new Task703Product { Name = "stol", Code = "c" } }).GetAwaiter().GetResult();
        ElasticLive.Refresh();

        var found = bulk.ReadAsync(x => x.Name == "stolicka", null, null, null).GetAwaiter().GetResult().ToList();

        found.Select(x => x.Guid).Should().Equal(stolicka.Guid);
    }

    private static IEnumerable<QueryBase> Flatten(QueryBase query)
    {
        yield return query;
        if (query is not BoolQuery b) yield break;
        foreach (var child in new[] { b.Must, b.Filter, b.Should, b.MustNot }.Where(c => c != null).SelectMany(c => c!))
        {
            if (child is IQueryContainer container && Unwrap(container) is QueryBase inner)
            {
                foreach (var q in Flatten(inner)) yield return q;
            }
        }
    }

    private static QueryBase? Unwrap(IQueryContainer container)
        => (QueryBase?)container.Bool ?? (QueryBase?)container.Terms ?? (QueryBase?)container.Term ?? (QueryBase?)container.Exists;

    private sealed class Slovak : IEntityLocalizationContext
    {
        public CultureInfo CurrentCulture { get; } = new("sk");
        public CultureInfo DefaultCulture { get; } = new("en");
    }

    public void Dispose() => _live.Dispose();
}
