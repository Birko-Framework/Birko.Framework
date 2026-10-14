using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Birko.Data.Stores;
using Birko.Data.Views;
using FluentAssertions;
using Nest;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.ElasticSearch.Views.Tests;

/// <summary>
/// TASK-615, live: the view store's <c>orderBy</c> names the stored field. It sent the CLR member name, a 400
/// "No mapping found for [Rank] in order to sort on" for any sorted view query.
/// <para>Gated on <c>BIRKO_ES_URL</c>; set <c>BIRKO_REQUIRE_LIVE</c> to make its absence a failure.</para>
/// </summary>
public sealed class ViewSortLiveTests : IDisposable
{
    private static readonly HttpClient Http = new();
    private static string? Url => Environment.GetEnvironmentVariable("BIRKO_ES_URL");
    private readonly ITestOutputHelper _output;
    private readonly string _index = "birko615view" + Guid.NewGuid().ToString("N");

    public ViewSortLiveTests(ITestOutputHelper output) => _output = output;

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(Url)) return;
        try { Http.DeleteAsync(Url.TrimEnd('/') + "/" + _index).GetAwaiter().GetResult(); } catch { }
    }

    public class RankedView
    {
        public int Rank { get; set; }
        public string? Name { get; set; }
    }

    private ViewDefinition Definition() => new(
        name: _index,
        queryMode: ViewQueryMode.Persistent,
        primarySource: typeof(RankedView),
        viewType: typeof(RankedView),
        fields: new List<FieldSelector>(),
        joins: new List<JoinClause>(),
        aggregates: new List<AggregateClause>(),
        groupBy: new List<GroupByClause>(),
        hints: new Dictionary<string, object>());

    [Fact]
    public async Task A_sorted_view_query_returns_rows_in_order()
    {
        if (string.IsNullOrWhiteSpace(Url))
        {
            _output.WriteLine("SKIPPED: no live Elasticsearch. Set BIRKO_ES_URL.");
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
                throw new InvalidOperationException("BIRKO_REQUIRE_LIVE is set but BIRKO_ES_URL is not.");
            return;
        }

        foreach (var (rank, name) in new[] { (2, "b"), (3, "a"), (1, "c") })
        {
            var body = new StringContent($"{{\"rank\":{rank},\"name\":\"{name}\"}}", Encoding.UTF8, "application/json");
            (await Http.PostAsync($"{Url.TrimEnd('/')}/{_index}/_doc?refresh=true", body)).EnsureSuccessStatusCode();
        }

        var store = new ElasticSearchViewStore<RankedView>(new ElasticClient(new ConnectionSettings(new Uri(Url))), Definition());

        (await store.QueryAsync(null, OrderBy<RankedView>.ByDescending(x => x.Rank))).Select(x => x.Rank).Should().Equal(3, 2, 1);
        (await store.QueryAsync(null, OrderBy<RankedView>.By(x => x.Name))).Select(x => x.Name).Should().Equal("a", "b", "c");
    }
}
