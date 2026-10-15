using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Birko.Data.ElasticSearch.Stores;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.ElasticSearch.Tests;

/// <summary>
/// TASK-634: <c>PlanMappingRepairAsync</c> turns mapping drift into requests a host sends itself. Measured against a real
/// ElasticSearch by sending exactly those requests, re-reading the drift, and querying through the store.
/// </summary>
public sealed class MappingRepairPlanLiveTests : IDisposable
{
    private static readonly HttpClient Http = new();
    private static string Url => Environment.GetEnvironmentVariable("BIRKO_ES_URL")!.TrimEnd('/');
    private readonly ElasticLive _live;
    private readonly ITestOutputHelper _output;

    public MappingRepairPlanLiveTests(ITestOutputHelper output)
    {
        _output = output;
        _live = new ElasticLive(output);
    }

    public void Dispose() => _live.Dispose();

    private AsyncElasticSearchStore<DriftDoc> NewStore()
    {
        var store = new AsyncElasticSearchStore<DriftDoc>();
        store.SetSettings(_live.NewSettings());
        return store;
    }

    private static async Task Put(string path, string json)
        => (await Http.PutAsync(Url + path, new StringContent(json, Encoding.UTF8, "application/json"))).EnsureSuccessStatusCode();

    private static async Task Send(MappingRepairRequest request)
    {
        var message = new HttpRequestMessage(new HttpMethod(request.Method), Url + request.Path)
        {
            Content = new StringContent(request.Body, Encoding.UTF8, "application/json")
        };
        (await Http.SendAsync(message)).EnsureSuccessStatusCode();
    }

    /// <summary>An index created before the model gained <c>code</c> and <c>rank</c>, holding one document of that time.</summary>
    private async Task<Guid> IndexThatPredatesTwoFields(AsyncElasticSearchStore<DriftDoc> store)
    {
        var index = store.GetIndexName();
        await Put("/" + index, @"{""mappings"":{""properties"":{
            ""guid"":{""type"":""keyword""},
            ""name"":{""type"":""text"",""fields"":{""keyword"":{""type"":""keyword"",""ignore_above"":256}}}}}}");
        var old = Guid.NewGuid();
        await Put($"/{index}/_doc/{old}", $@"{{""guid"":""{old}"",""name"":""old""}}");
        ElasticLive.Refresh();
        return old;
    }

    [Fact]
    public async Task The_plan_adds_the_missing_fields_and_the_new_field_is_queryable()
    {
        if (!_live.Ready()) return;
        var store = NewStore();
        var old = await IndexThatPredatesTwoFields(store);

        var plan = await store.PlanMappingRepairAsync();
        foreach (var r in plan.Requests) _output.WriteLine(r.ToString());
        foreach (var r in plan.Requests) await Send(r);

        plan.Requests.Should().ContainSingle().Which.Method.Should().Be("PUT");
        plan.IsComplete.Should().BeTrue();
        (await store.DetectMappingDriftAsync()).Drifts.Should().BeEmpty("a re-run of the detection finds nothing left");

        await store.CreateAsync(new DriftDoc { Code = "X-1", Rank = 2, Name = "new" });
        ElasticLive.Refresh();
        (await store.ReadAsync(x => x.Code == "X-1", null, null, null)).Should().ContainSingle("a filter on the added keyword field matches");
        // Documents indexed before the repair have no value for the new fields: they read back with the defaults and
        // match no filter on them, which is what an entity that never had the property should do.
        var before = (await store.ReadRealtimeAsync(old))!;
        before.Rank.Should().Be(0);
        before.Code.Should().BeEmpty();
        (await store.ReadAsync(x => x.Rank == 0, null, null, null)).Should().BeEmpty("the old document was indexed without the field");
    }

    [Fact]
    public async Task A_type_change_and_an_undeclared_field_are_listed_not_written()
    {
        if (!_live.Ready()) return;
        var store = NewStore();
        await Put("/" + store.GetIndexName(), @"{""mappings"":{""properties"":{
            ""guid"":{""type"":""keyword""},
            ""code"":{""type"":""text"",""fields"":{""keyword"":{""type"":""keyword"",""ignore_above"":256}}},
            ""rank"":{""type"":""keyword""},
            ""legacy"":{""type"":""text""}}}}");

        var plan = await store.PlanMappingRepairAsync();
        foreach (var r in plan.Requests) await Send(r);

        plan.NotActioned.Select(s => s.Drift).Should().Contain(d => d.StartsWith(store.GetIndexName() + ".code:"));
        plan.NotActioned.Select(s => s.Drift).Should().Contain(d => d.Contains(".rank:"));
        plan.NotActioned.Select(s => s.Drift).Should().Contain(d => d.Contains(".legacy:"));
        plan.NotActioned.Should().Contain(s => s.Reason.Contains("_reindex"));
        plan.Requests.Should().OnlyContain(r => !r.Body.Contains("\"code\"") && !r.Body.Contains("\"rank\""),
            "a field with a type mismatch is not sent: the merge would be refused, and it needs the reindex");
        (await store.DetectMappingDriftAsync()).Drifts.Select(d => d.Field)
            .Should().BeEquivalentTo(new[] { "code", "code.keyword", "rank", "legacy" }, "only what the plan could add was added");
    }

    /// <summary>Mutation proof: a plan body with the wrong type is caught by the re-check the plan is verified with.</summary>
    [Fact]
    public async Task A_wrong_type_in_the_body_fails_the_recheck()
    {
        if (!_live.Ready()) return;
        var store = NewStore();
        await IndexThatPredatesTwoFields(store);
        var request = (await store.PlanMappingRepairAsync()).Requests.Single();

        await Send(new MappingRepairRequest(request.Method, request.Path, request.Body.Replace("\"keyword\"", "\"text\"")));

        (await store.DetectMappingDriftAsync()).Drifts.Should().Contain(d => d.Field == "code" && d.Kind == MappingDriftKind.TypeMismatch);
    }
}
