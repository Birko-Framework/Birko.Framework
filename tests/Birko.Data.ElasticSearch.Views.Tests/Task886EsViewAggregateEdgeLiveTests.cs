using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Birko.Data.Views;
using FluentAssertions;
using Nest;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.ElasticSearch.Views.Tests;

/// <summary>The test below changes a cluster setting, so it runs alone, never beside another class of this suite.</summary>
[CollectionDefinition("Task886EsIdFieldData", DisableParallelization = true)]
public sealed class Task886EsIdFieldDataCollection { }

/// <summary>
/// TASK-886, live Elasticsearch: a view's <c>Count</c> is the number of rows, and an ungrouped aggregate view over no rows
/// answers one row (Count 0, Sum 0, Min / Avg null); a SUM over only NULLs is 0.
/// <para>
/// Measured before: Count was a <c>value_count</c> on <c>_id</c>, which counts rows only while the cluster lets <c>_id</c>
/// be aggregated. With <c>indices.id_field_data.enabled: false</c> (the Elasticsearch 8 default, set here on the 7.17
/// cluster for the test) every Count view failed (illegal_argument_exception on <c>_id</c>). Count is now the bucket's
/// document count, or the hit total for an ungrouped view. Gated on <c>BIRKO_ES_URL</c>.
/// </para>
/// </summary>
[Collection("Task886EsIdFieldData")]
public sealed class Task886EsViewAggregateEdgeLiveTests : IAsyncLifetime
{
    private static readonly HttpClient Http = new();
    private static string? Url => Environment.GetEnvironmentVariable("BIRKO_ES_URL");
    private readonly ITestOutputHelper _output;
    private ElasticClient? _client;

    public Task886EsViewAggregateEdgeLiveTests(ITestOutputHelper output) => _output = output;

    public class T886EsRow
    {
        public string? Grp { get; set; }
        public int? Score { get; set; }
    }

    public class T886EsEmpty
    {
        public string? Grp { get; set; }
        public int? Score { get; set; }
    }

    public class WholeTotals
    {
        public long Cnt { get; set; }
        public int? SumScore { get; set; }
        public int? MinScore { get; set; }
        public double? AvgScore { get; set; }
    }

    public class GroupTotals
    {
        public string? Grp { get; set; }
        public long Cnt { get; set; }
        public int? SumScore { get; set; }
        public int? MinScore { get; set; }
    }

    private const string Rows = "t886esrow";
    private const string Empty = "t886esempty";

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
        await Http.DeleteAsync($"{Url.TrimEnd('/')}/{Empty}");
        (await _client.Indices.CreateAsync(Empty, c => c.Map<T886EsEmpty>(m => m.AutoMap()))).IsValid.Should().BeTrue();
        foreach (var (grp, score) in new[] { ("a", (int?)null), ("a", null), ("b", 4) })
        {
            (await _client.IndexAsync(new T886EsRow { Grp = grp, Score = score }, i => i.Index(Rows).Refresh(Elasticsearch.Net.Refresh.True)))
                .IsValid.Should().BeTrue();
        }
    }

    public async Task DisposeAsync()
    {
        if (string.IsNullOrWhiteSpace(Url)) return;
        try { await Http.DeleteAsync($"{Url.TrimEnd('/')}/{Rows}"); } catch { }
        try { await Http.DeleteAsync($"{Url.TrimEnd('/')}/{Empty}"); } catch { }
    }

    private static ViewDefinition Whole<TSource>() where TSource : class => new ViewDefinitionBuilder<WholeTotals>()
        .From<TSource>()
        .Count<TSource>(v => v.Cnt)
        .Sum<TSource, int?>(Score<TSource>(), v => v.SumScore)
        .Min<TSource, int?>(Score<TSource>(), v => v.MinScore)
        .Avg<TSource, double?>(Score<TSource, double?>(), v => v.AvgScore)
        .Build();

    private static System.Linq.Expressions.Expression<Func<TSource, int?>> Score<TSource>() => Score<TSource, int?>();

    private static System.Linq.Expressions.Expression<Func<TSource, TValue>> Score<TSource, TValue>()
    {
        var p = System.Linq.Expressions.Expression.Parameter(typeof(TSource), "r");
        var body = System.Linq.Expressions.Expression.Convert(System.Linq.Expressions.Expression.Property(p, "Score"), typeof(TValue));
        return System.Linq.Expressions.Expression.Lambda<Func<TSource, TValue>>(body, p);
    }

    private static ViewDefinition Grouped() => new ViewDefinitionBuilder<GroupTotals>()
        .From<T886EsRow>()
        .GroupBy<T886EsRow, string?>(r => r.Grp)
        .Select<T886EsRow, string?>(r => r.Grp, v => v.Grp)
        .Count<T886EsRow>(v => v.Cnt)
        .Sum<T886EsRow, int?>(r => r.Score, v => v.SumScore)
        .Min<T886EsRow, int?>(r => r.Score, v => v.MinScore)
        .Build();

    /// <summary>Sets the cluster's <c>indices.id_field_data.enabled</c>; null restores its default.</summary>
    private static async Task SetIdFieldData(bool? enabled)
    {
        var value = enabled == null ? "null" : enabled.Value ? "true" : "false";
        var body = $"{{\"persistent\":{{\"indices.id_field_data.enabled\":{value}}}}}";
        var response = await Http.PutAsync($"{Url!.TrimEnd('/')}/_cluster/settings", new StringContent(body, Encoding.UTF8, "application/json"));
        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_count_is_the_number_of_rows(bool idFieldData)
    {
        if (_client == null) return;
        await SetIdFieldData(idFieldData);
        try
        {
            (await new ElasticSearchViewStore<GroupTotals>(_client, Grouped()).QueryAsync()).OrderBy(g => g.Grp).Should().BeEquivalentTo(new[]
            {
                new GroupTotals { Grp = "a", Cnt = 2, SumScore = 0 },
                new GroupTotals { Grp = "b", Cnt = 1, SumScore = 4, MinScore = 4 },
            }, o => o.WithStrictOrdering());
            (await new ElasticSearchViewStore<WholeTotals>(_client, Whole<T886EsRow>()).QueryAsync())
                .Should().ContainSingle().Which.Should().BeEquivalentTo(new WholeTotals { Cnt = 3, SumScore = 4, MinScore = 4, AvgScore = 4 });
        }
        finally
        {
            await SetIdFieldData(null);
        }
    }

    [Fact]
    public async Task An_ungrouped_view_over_no_rows_answers_one_row()
    {
        if (_client == null) return;
        var view = new ElasticSearchViewStore<WholeTotals>(_client, Whole<T886EsEmpty>());

        (await view.QueryAsync()).Should().ContainSingle().Which.Should().BeEquivalentTo(new WholeTotals { Cnt = 0, SumScore = 0 });
        (await view.CountAsync()).Should().Be(1);
    }
}
