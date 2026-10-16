using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Birko.Data.ElasticSearch.Aggregation;
using Birko.Data.Stores;
using Birko.Data.Views;
using FluentAssertions;
using Nest;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.ElasticSearch.Views.Tests;

/// <summary>
/// TASK-151, live: a view reads its source under the view's names (SH-M382), groups under them (SH-M387, SH-M389),
/// orders, pages and counts the rows it returns (SH-M380, SH-M381), reads every group (SH-M390), keeps a date
/// minimum (SH-M388), ensures only its own index (SH-M391) and refuses a join (SH-M411).
/// <para>Gated on <c>BIRKO_ES_URL</c>; set <c>BIRKO_REQUIRE_LIVE</c> to make its absence a failure.</para>
/// </summary>
public sealed class Task151EsViewLiveTests : IAsyncLifetime
{
    private static readonly HttpClient Http = new();
    private static string? Url => Environment.GetEnvironmentVariable("BIRKO_ES_URL");
    private readonly ITestOutputHelper _output;
    private ElasticClient? _client;

    public Task151EsViewLiveTests(ITestOutputHelper output) => _output = output;

    public class T151EsOrder
    {
        public string? Code { get; set; }
        public string? Region { get; set; }
        public int Qty { get; set; }
        public DateTime Created { get; set; }
    }

    public class T151EsOther { public string? Code { get; set; } }

    public class StatusView { public string? Status { get; set; } public string? Area { get; set; } }
    public class AreaTotalView { public string? Area { get; set; } public int Total { get; set; } public long N { get; set; } }
    public class AreaCodeView { public string? Area { get; set; } public string? Status { get; set; } public int Total { get; set; } }
    public class EarliestView { public DateTime Earliest { get; set; } }

    private const string Index = "t151esorder";
    private static readonly DateTime First = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

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
        await Http.DeleteAsync($"{Url.TrimEnd('/')}/t151_summary");
        foreach (var (code, region, qty, created) in new[] { ("a", "EU", 1, First.AddDays(1)), ("b", "EU", 2, First), ("c", "US", 4, First.AddDays(2)) })
        {
            (await _client.IndexAsync(new T151EsOrder { Code = code, Region = region, Qty = qty, Created = created }, i => i.Index(Index).Refresh(Elasticsearch.Net.Refresh.True)))
                .IsValid.Should().BeTrue();
        }
    }

    public async Task DisposeAsync()
    {
        if (string.IsNullOrWhiteSpace(Url)) return;
        try { await Http.DeleteAsync($"{Url.TrimEnd('/')}/{Index}"); } catch { }
        try { await Http.DeleteAsync($"{Url.TrimEnd('/')}/t151_summary"); } catch { }
    }

    [Fact]
    public async Task A_plain_view_reads_filters_orders_and_counts_under_the_view_names()
    {
        if (_client == null) return;
        var definition = new ViewDefinitionBuilder<StatusView>()
            .From<T151EsOrder>()
            .Select<T151EsOrder, string?>(o => o.Code, v => v.Status)
            .Select<T151EsOrder, string?>(o => o.Region, v => v.Area)
            .Build();
        var store = new ElasticSearchViewStore<StatusView>(_client, definition);

        (await store.QueryAsync(null, OrderBy<StatusView>.ByDescending(v => v.Status))).Select(v => v.Status).Should().Equal("c", "b", "a");
        (await store.QueryAsync(v => v.Status == "b")).Should().ContainSingle().Which.Area.Should().Be("EU");
        (await store.CountAsync(v => v.Area == "EU")).Should().Be(2);
    }

    private static ViewDefinition Totals() => new ViewDefinitionBuilder<AreaTotalView>()
        .From<T151EsOrder>()
        .GroupBy<T151EsOrder, string?>(o => o.Region)
        .Select<T151EsOrder, string?>(o => o.Region, v => v.Area)
        .Sum<T151EsOrder, int>(o => o.Qty, v => v.Total)
        .Count<T151EsOrder>(v => v.N)
        .Build();

    [Fact]
    public async Task A_grouped_view_keys_its_rows_and_orders_pages_and_counts_them()
    {
        if (_client == null) return;
        var store = new ElasticSearchViewStore<AreaTotalView>(_client, Totals());

        (await store.QueryAsync()).Should().BeEquivalentTo(new[]
        {
            new AreaTotalView { Area = "EU", Total = 3, N = 2 },
            new AreaTotalView { Area = "US", Total = 4, N = 1 },
        });
        (await store.QueryAsync(null, OrderBy<AreaTotalView>.ByDescending(v => v.Total), limit: 1, offset: 1))
            .Should().ContainSingle().Which.Area.Should().Be("EU");
        (await store.CountAsync()).Should().Be(2, "two groups, not three documents");
        (await store.CountAsync(v => v.Area == "US")).Should().Be(1);
    }

    [Fact]
    public async Task Every_group_is_read_however_many_pages_it_takes()
    {
        if (_client == null) return;
        var definition = new ViewDefinitionBuilder<AreaCodeView>()
            .From<T151EsOrder>()
            .GroupBy<T151EsOrder, string?>(o => o.Region)
            .GroupBy<T151EsOrder, string?>(o => o.Code)
            .Select<T151EsOrder, string?>(o => o.Region, v => v.Area)
            .Select<T151EsOrder, string?>(o => o.Code, v => v.Status)
            .Sum<T151EsOrder, int>(o => o.Qty, v => v.Total)
            .Build();
        var store = new ElasticSearchViewStore<AreaCodeView>(_client, definition) { GroupPageSize = 2 };

        (await store.QueryAsync()).Should().BeEquivalentTo(new[]
        {
            new AreaCodeView { Area = "EU", Status = "a", Total = 1 },
            new AreaCodeView { Area = "EU", Status = "b", Total = 2 },
            new AreaCodeView { Area = "US", Status = "c", Total = 4 },
        });
    }

    [Fact]
    public async Task A_date_minimum_is_a_date()
    {
        if (_client == null) return;
        var definition = new ViewDefinitionBuilder<EarliestView>()
            .From<T151EsOrder>()
            .Min<T151EsOrder, DateTime>(o => o.Created, v => v.Earliest)
            .Build();

        (await new ElasticSearchViewStore<EarliestView>(_client, definition).QueryFirstAsync())!.Earliest.Should().Be(First);
    }

    [Fact]
    public async Task Ensure_creates_only_a_persistent_views_own_index()
    {
        if (_client == null) return;
        await Http.DeleteAsync($"{Url!.TrimEnd('/')}/{Index}");
        var manager = new ElasticSearchViewManager(_client);
        ViewDefinition Named(ViewQueryMode mode) => new ViewDefinitionBuilder<StatusView>()
            .HasName("t151_summary").HasQueryMode(mode)
            .From<T151EsOrder>()
            .Select<T151EsOrder, string?>(o => o.Code, v => v.Status)
            .Build();

        await manager.EnsureAsync(Named(ViewQueryMode.Auto));
        (await _client.Indices.ExistsAsync(Index)).Exists.Should().BeFalse("ensuring a view must not create its source index");

        await manager.EnsureAsync(Named(ViewQueryMode.Persistent));
        (await manager.ExistsAsync("t151_summary")).Should().BeTrue();
    }

    [Fact]
    public void A_join_is_refused()
    {
        var definition = new ViewDefinitionBuilder<StatusView>()
            .From<T151EsOrder>()
            .Join<T151EsOrder, T151EsOther, string?>(o => o.Code, x => x.Code)
            .Select<T151EsOrder, string?>(o => o.Code, v => v.Status)
            .Build();

        FluentActions.Invoking(() => new ElasticSearchViewStore<StatusView>(new ElasticClient(), definition)).Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Truncated_groups_are_reported_not_returned_as_complete()
    {
        StoreAggregationHelper.TruncationError(sumOtherDocCount: 5, bucketCount: 10, size: 10000).Should().NotBeNull();
        StoreAggregationHelper.TruncationError(sumOtherDocCount: null, bucketCount: 10000, size: 10000).Should().NotBeNull();
        StoreAggregationHelper.TruncationError(sumOtherDocCount: 0, bucketCount: 12, size: 10000).Should().BeNull();
    }
}
