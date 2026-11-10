using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Birko.Data.Views;
using FluentAssertions;
using Nest;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.ElasticSearch.Views.Tests;

/// <summary>
/// TASK-907, live Elasticsearch: a view's string Min / Max is Ordinal, as the store's <c>Aggregate</c> is by default (the
/// user's decision, 2026-11-10), taken as the first <c>terms</c> bucket of the keyword. A keyword orders by UTF-8 bytes,
/// code-point order: Ordinal but for a character above U+FFFF against one in U+E000–U+FFFF, a group holding both is refused by name. Measured before:
/// the view sent a <c>min</c> aggregation over the keyword, which Elasticsearch refuses with a 400. Gated on
/// <c>BIRKO_ES_URL</c>.
/// </summary>
public sealed class Task907EsViewStringOrderLiveTests : IAsyncLifetime
{
    private static readonly HttpClient Http = new();
    private static string? Url => Environment.GetEnvironmentVariable("BIRKO_ES_URL");
    private readonly ITestOutputHelper _output;
    private ElasticClient? _client;

    public Task907EsViewStringOrderLiveTests(ITestOutputHelper output) => _output = output;

    public class T907EsRow
    {
        public string? Kind { get; set; }
        public string? Name { get; set; }
    }

    public class Extremes
    {
        public string? Kind { get; set; }
        public string? Low { get; set; }
        public string? High { get; set; }
    }

    public class WholeExtremes
    {
        public string? Low { get; set; }
        public string? High { get; set; }
    }

    private const string Rows = "t907esrow";

    private static readonly (string Kind, string? Name)[] Seed =
    {
        ("case", "apple"), ("case", "Banana"),
        ("accent", "éclair"), ("accent", "zebra"),
        ("word", "zebra"), ("word", "Zebra"),
        ("space", "a "), ("space", "a"),
        ("eszett", "ß"), ("eszett", "ss"),
        ("nulls", null), ("nulls", "m"),
        ("allnull", null),
    };

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
        await Http.DeleteAsync($"{Url.TrimEnd('/')}/{Rows}");
        foreach (var (kind, name) in Seed)
        {
            (await _client.IndexAsync(new T907EsRow { Kind = kind, Name = name }, i => i.Index(Rows).Refresh(Elasticsearch.Net.Refresh.True)))
                .IsValid.Should().BeTrue();
        }
    }

    public async Task DisposeAsync()
    {
        if (string.IsNullOrWhiteSpace(Url)) return;
        try { await Http.DeleteAsync($"{Url.TrimEnd('/')}/{Rows}"); } catch { }
    }

    private static string Show(string? value)
        => value == null ? "null" : "\"" + string.Concat(value.Select(c => c < 0x20 || c > 0x7E ? $"\\u{(int)c:X4}" : c.ToString())) + "\"";

    [Fact]
    public async Task A_grouped_view_string_min_and_max_are_Ordinal()
    {
        if (_client == null) return;
        var definition = new ViewDefinitionBuilder<Extremes>()
            .From<T907EsRow>()
            .GroupBy<T907EsRow, string?>(r => r.Kind)
            .Select<T907EsRow, string?>(r => r.Kind, v => v.Kind)
            .Min<T907EsRow, string?>(r => r.Name, v => v.Low)
            .Max<T907EsRow, string?>(r => r.Name, v => v.High)
            .Build();

        var expected = string.Join(", ", Seed.GroupBy(s => s.Kind).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g =>
        {
            var values = g.Select(s => s.Name).Where(n => n != null).ToList();
            return $"{g.Key}={Show(values.Count == 0 ? null : values.Min(StringComparer.Ordinal))}..{Show(values.Count == 0 ? null : values.Max(StringComparer.Ordinal))}";
        }));
        string actual;
        try
        {
            var rows = (await new ElasticSearchViewStore<Extremes>(_client, definition).QueryAsync()).ToList();
            actual = string.Join(", ", rows.OrderBy(r => r.Kind, StringComparer.Ordinal).Select(r => $"{r.Kind}={Show(r.Low)}..{Show(r.High)}"));
        }
        catch (Exception ex) { actual = $"{ex.GetType().Name}: {ex.Message}"; }

        _output.WriteLine($"MEASURE | Elasticsearch | {actual} | expected {expected}");
        actual.Should().Be(expected);
    }

    [Fact]
    public async Task An_ungrouped_view_string_min_and_max_are_Ordinal()
    {
        if (_client == null) return;
        var definition = new ViewDefinitionBuilder<WholeExtremes>()
            .From<T907EsRow>()
            .Min<T907EsRow, string?>(r => r.Name, v => v.Low)
            .Max<T907EsRow, string?>(r => r.Name, v => v.High)
            .Build();

        var row = (await new ElasticSearchViewStore<WholeExtremes>(_client, definition).QueryAsync()).Should().ContainSingle().Subject;

        (row.Low, row.High).Should().Be(("Banana", "éclair"));
    }

    public class T907EsAstral
    {
        public string? Kind { get; set; }
        public string? Name { get; set; }
    }

    /// <summary>
    /// A keyword orders by UTF-8 bytes, code-point order: Ordinal but for a group holding both a character above U+FFFF and
    /// one in U+E000–U+FFFF, which is refused by name; a group of either kind alone is answered exactly. Before: the mixed
    /// group answered U+FF5E as its Min (the store pinned it).
    /// </summary>
    [Theory]
    [InlineData("mixed")]
    [InlineData("astral")]
    [InlineData("bmp")]
    public async Task An_astral_group_is_answered_exactly_or_refused_when_mixed(string kind)
    {
        if (_client == null) return;
        const string index = "t907esastral";
        await Http.DeleteAsync($"{Url!.TrimEnd('/')}/{index}");
        try
        {
            var seed = new[] { ("mixed", "\U0001F600"), ("mixed", "\uFF5E"), ("astral", "\U0001F600"), ("astral", "\U0001F4A9"), ("bmp", "\uFF5E"), ("bmp", "\uE000") };
            foreach (var (k, n) in seed)
            {
                (await _client.IndexAsync(new T907EsAstral { Kind = k, Name = n }, i => i.Index(index).Refresh(Elasticsearch.Net.Refresh.True))).IsValid.Should().BeTrue();
            }
            var definition = new ViewDefinitionBuilder<Extremes>()
                .From<T907EsAstral>()
                .GroupBy<T907EsAstral, string?>(r => r.Kind)
                .Select<T907EsAstral, string?>(r => r.Kind, v => v.Kind)
                .Min<T907EsAstral, string?>(r => r.Name, v => v.Low)
                .Max<T907EsAstral, string?>(r => r.Name, v => v.High)
                .Build();

            var act = async () => (await new ElasticSearchViewStore<Extremes>(_client, definition).QueryAsync(v => v.Kind == kind)).ToList();

            if (kind == "mixed")
            {
                (await act.Should().ThrowAsync<NotSupportedException>()).Which.Message.Should().Contain("U+FFFF");
                return;
            }
            var values = seed.Where(s => s.Item1 == kind).Select(s => s.Item2).ToList();
            var row = (await act()).Single();
            (row.Low, row.High).Should().Be((values.Min(StringComparer.Ordinal), values.Max(StringComparer.Ordinal)));
        }
        finally
        {
            try { await Http.DeleteAsync($"{Url!.TrimEnd('/')}/{index}"); } catch { }
        }
    }
}
