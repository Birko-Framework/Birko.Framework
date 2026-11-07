using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Birko.Data.Stores;
using Birko.Data.Views;
using FluentAssertions;
using Nest;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.ElasticSearch.Views.Tests;

/// <summary>
/// TASK-843: a view that projects a nested member (<c>r.Sub.X</c>) was recorded as source field <c>X</c> by the
/// shared builder, so Elasticsearch read the document's top-level <c>x</c> (measured on Elasticsearch 7.17,
/// 2026-11-07: every nested view property read its same-named top-level field). The read now includes and walks the
/// whole path (<c>sub.x</c>), and a sort on it sorts by the stored sub-field. A filter on it is refused by name: the
/// entity store's translator does not translate a member below the document (SH-M142).
/// Gated on <c>BIRKO_ES_URL</c>; set <c>BIRKO_REQUIRE_LIVE</c> to make its absence a failure.
/// </summary>
public sealed class Task843EsViewNestedProjectionLiveTests : IAsyncLifetime
{
    private static readonly HttpClient Http = new();
    private static string? Url => Environment.GetEnvironmentVariable("BIRKO_ES_URL");
    private readonly ITestOutputHelper _output;
    private ElasticClient? _client;
    private readonly List<FlatView> _oracle = new();

    public Task843EsViewNestedProjectionLiveTests(ITestOutputHelper output) => _output = output;

    public class T843Deeper { public int Level { get; set; } }

    public class T843Sub
    {
        public int X { get; set; }
        public string? Name { get; set; }
        public T843Deeper? Deep { get; set; }
    }

    /// <summary>Top-level members named like the nested ones, with different values, so a flattened read shows.</summary>
    public class T843EsRow
    {
        public int X { get; set; }
        public string? Name { get; set; }
        public int Level { get; set; }
        public T843Sub Sub { get; set; } = new();
    }

    public class FlatView
    {
        public int TopX { get; set; }
        public int SubX { get; set; }
        public string? SubName { get; set; }
        public int? Level { get; set; }
    }

    private const string Index = "t843esrow";

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(Url))
        {
            _output.WriteLine("SKIPPED: no live Elasticsearch. Set BIRKO_ES_URL.");
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
                throw new InvalidOperationException("BIRKO_REQUIRE_LIVE is set but BIRKO_ES_URL is not.");
            return;
        }
        _client = new ElasticClient(new ConnectionSettings(new Uri(Url)));
        await Http.DeleteAsync($"{Url.TrimEnd('/')}/{Index}");
        for (var i = 0; i < 6; i++)
        {
            var row = new T843EsRow
            {
                X = 10 + i, Name = "top" + i, Level = 100 + i,
                Sub = new T843Sub { X = 5 - i, Name = "n" + i, Deep = i == 3 ? null : new T843Deeper { Level = i % 3 } },
            };
            (await _client.IndexAsync(row, d => d.Index(Index).Refresh(Elasticsearch.Net.Refresh.True))).IsValid.Should().BeTrue();
            _oracle.Add(new FlatView { TopX = row.X, SubX = row.Sub.X, SubName = row.Sub.Name, Level = row.Sub.Deep?.Level });
        }
    }

    public async Task DisposeAsync()
    {
        if (string.IsNullOrWhiteSpace(Url)) return;
        try { await Http.DeleteAsync($"{Url.TrimEnd('/')}/{Index}"); } catch { }
    }

    private static ViewDefinition Nested() => new ViewDefinitionBuilder<FlatView>()
        .From<T843EsRow>()
        .Select<T843EsRow, int>(r => r.X, v => v.TopX)
        .Select<T843EsRow, int>(r => r.Sub.X, v => v.SubX)
        .Select<T843EsRow, string?>(r => r.Sub.Name, v => v.SubName)
        .Select<T843EsRow, int?>(r => r.Sub.Deep!.Level, v => v.Level)
        .Build();

    private static IEnumerable<(int, int, string?, int?)> Tuples(IEnumerable<FlatView> rows)
        => rows.Select(v => (v.TopX, v.SubX, v.SubName, v.Level));

    [Fact]
    public async Task A_nested_projection_reads_the_nested_values()
    {
        if (_client == null) return;
        var rows = (await new ElasticSearchViewStore<FlatView>(_client, Nested()).QueryAsync()).ToList();
        Tuples(rows).Should().BeEquivalentTo(Tuples(_oracle));
    }

    [Fact]
    public async Task An_order_by_a_nested_projection_sorts_by_the_nested_value()
    {
        if (_client == null) return;
        var rows = (await new ElasticSearchViewStore<FlatView>(_client, Nested()).QueryAsync(null, OrderBy<FlatView>.ByDescending(v => v.SubX))).ToList();
        rows.Select(v => v.SubX).Should().Equal(_oracle.Select(v => v.SubX).OrderByDescending(x => x));
    }

    [Fact]
    public async Task A_filter_on_a_nested_projection_is_refused_by_name()
    {
        if (_client == null) return;
        var store = new ElasticSearchViewStore<FlatView>(_client, Nested());
        (await store.Invoking(s => s.QueryAsync(v => v.SubX >= 3)).Should().ThrowAsync<NotSupportedException>())
            .WithMessage("*FlatView.SubX*Sub.X*");
        (await store.Invoking(s => s.CountAsync(v => v.SubX >= 3)).Should().ThrowAsync<NotSupportedException>())
            .WithMessage("*FlatView.SubX*Sub.X*");
    }
}
