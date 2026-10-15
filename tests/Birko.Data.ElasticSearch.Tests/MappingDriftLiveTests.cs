using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Birko.Data.ElasticSearch.Stores;
using Birko.Data.Models;
using FluentAssertions;
using Nest;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.ElasticSearch.Tests;

/// <summary>The model as it is today: <c>Code</c> was made <c>[Keyword]</c> after the index existed.</summary>
public class DriftDoc : AbstractModel
{
    [Keyword(Name = "code")]
    public string Code { get; set; } = string.Empty;

    public int Rank { get; set; }

    public string? Name { get; set; }
}

/// <summary>
/// TASK-621, live. An index is created once from the model's mapping and never revisited, so a model changed later
/// keeps querying an index mapped for the old one. Nothing reported it: TASK-613's <c>.keyword</c> defect lived in
/// exactly that gap.
/// </summary>
public sealed class MappingDriftLiveTests : IDisposable
{
    private static readonly HttpClient Http = new();
    private static string Url => Environment.GetEnvironmentVariable("BIRKO_ES_URL")!.TrimEnd('/');
    private readonly ElasticLive _live;
    public MappingDriftLiveTests(ITestOutputHelper output) => _live = new ElasticLive(output);
    public void Dispose() => _live.Dispose();

    private AsyncElasticSearchStore<DriftDoc> NewStore()
    {
        var store = new AsyncElasticSearchStore<DriftDoc>();
        store.SetSettings(_live.NewSettings());
        return store;
    }

    /// <summary>The index an older model created: <c>code</c> dynamic text with a keyword sub-field, <c>rank</c> a keyword.</summary>
    private static async Task CreateOldIndex(string index)
    {
        const string mapping = @"{""mappings"":{""properties"":{
            ""guid"":{""type"":""keyword""},
            ""code"":{""type"":""text"",""fields"":{""keyword"":{""type"":""keyword"",""ignore_above"":256}}},
            ""rank"":{""type"":""keyword""},
            ""legacy"":{""type"":""text""}}}}";
        (await Http.PutAsync($"{Url}/{index}", new StringContent(mapping, Encoding.UTF8, "application/json"))).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task An_index_the_store_created_reports_clean()
    {
        if (!_live.Ready()) return;
        var store = NewStore();
        await store.CreateAsync(new DriftDoc { Code = "A", Rank = 1, Name = "n" });

        var report = await store.DetectMappingDriftAsync();
        foreach (var d in report.Drifts) Console.WriteLine(d);

        report.Supported.Should().BeTrue();
        report.IndexExists.Should().BeTrue();
        report.Drifts.Should().BeEmpty("every field was mapped by the descriptor the comparison is built from");
        report.IsClean.Should().BeTrue();
    }

    /// <summary>
    /// The premise, measured: on the old index a filter on the now-<c>[Keyword]</c> member silently finds nothing.
    /// The report then names every disagreement.
    /// </summary>
    [Fact]
    public async Task An_index_mapped_for_an_older_model_is_reported_field_by_field()
    {
        if (!_live.Ready()) return;
        var store = NewStore();
        await CreateOldIndex(store.GetIndexName());
        await store.CreateAsync(new DriftDoc { Code = "A-1", Rank = 1, Name = "n" });
        ElasticLive.Refresh();

        (await store.ReadAsync(x => x.Code == "A-1")).Should().BeEmpty("the query targets `code` as a keyword; the index analysed it as text");

        var report = await store.DetectMappingDriftAsync();

        report.IsClean.Should().BeFalse();
        report.Drifts.Select(d => (d.Field, d.Kind)).Should().BeEquivalentTo(new[]
        {
            ("code", MappingDriftKind.TypeMismatch),          // keyword declared, text stored
            ("code.keyword", MappingDriftKind.Unexpected),    // the old dynamic sub-field
            ("rank", MappingDriftKind.TypeMismatch),          // integer declared, keyword stored
            ("legacy", MappingDriftKind.Unexpected),
            // `name` is absent: the write mapped it dynamically (text + .keyword), which is what AutoMap declares.
        }, o => o.WithoutStrictOrdering());
        report.Drifts.Single(d => d.Field == "code").Declared.Should().Be("keyword");
        report.Drifts.Single(d => d.Field == "code").Stored.Should().Be("text");
    }

    [Fact]
    public async Task A_store_whose_index_does_not_exist_yet_is_not_drift()
    {
        if (!_live.Ready()) return;
        var report = await NewStore().DetectMappingDriftAsync();

        report.Supported.Should().BeTrue();
        report.IndexExists.Should().BeFalse();
        report.Drifts.Should().BeEmpty();
        report.IsClean.Should().BeFalse("an index nobody created is not a clean bill of health");
    }

    /// <summary>The declared side is the descriptor the store created its index with, never a fixed AutoMap.</summary>
    [Fact]
    public async Task A_custom_index_descriptor_is_what_the_comparison_declares()
    {
        if (!_live.Ready()) return;
        var store = NewStore();
        await store.InitAsync(c => c.Map<DriftDoc>(m => m.AutoMap().Properties(p => p.Keyword(k => k.Name(n => n.Name)))));

        (await store.DetectMappingDriftAsync()).Drifts.Should().BeEmpty();
    }

    /// <summary>
    /// The control most likely to false-positive: renamed (<c>[Keyword(Name = "ownerRef")]</c>) and unindexed
    /// (<c>[Text(Index = false)]</c>) fields, the shape of the workflow, job and sync models. Read back from the index the
    /// store created, they must compare clean.
    /// </summary>
    [Fact]
    public async Task Attribute_mapped_models_report_clean_on_the_index_their_store_created()
    {
        if (!_live.Ready()) return;
        var store = new AsyncElasticSearchStore<MappedNamesModel>();
        store.SetSettings(_live.NewSettings());
        await store.CreateAsync(new MappedNamesModel { Code = "c", Owner = "o", Name = "n", Notes = "x" });

        (await store.DetectMappingDriftAsync()).Drifts.Should().BeEmpty();
    }

    [Fact]
    public void The_sync_store_reports_the_same_drift()
    {
        if (!_live.Ready()) return;
        var store = new ElasticSearchStore<DriftDoc>();
        store.SetSettings(_live.NewSettings());
        CreateOldIndex(store.GetIndexName()).GetAwaiter().GetResult();

        // Unlike the async case no document is written, so `name` was never mapped dynamically: it is Missing.
        store.DetectMappingDrift().Drifts.Select(d => (d.Field, d.Kind)).Should().BeEquivalentTo(new[]
        {
            ("code", MappingDriftKind.TypeMismatch),
            ("code.keyword", MappingDriftKind.Unexpected),
            ("rank", MappingDriftKind.TypeMismatch),
            ("legacy", MappingDriftKind.Unexpected),
            ("name", MappingDriftKind.Missing),
            ("name.keyword", MappingDriftKind.Missing),
        });
    }
}
