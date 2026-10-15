using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.ElasticSearch.Stores;
using Birko.Data.Models;
using Birko.Health;
using Birko.Health.Data.ElasticSearch;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Health.Data.ElasticSearch.Tests;

/// <summary>TASK-624: what the check reports for each kind of answer a probe gives. Offline, with stub reports.</summary>
public class ElasticSearchMappingDriftHealthCheckTests
{
    private static Func<CancellationToken, Task<MappingDriftReport>> Probe(MappingDriftReport report) => _ => Task.FromResult(report);

    private static MappingDriftReport Clean(string index) => new(index, true, true, Array.Empty<MappingDrift>());

    [Fact]
    public async Task Clean_stores_are_Healthy()
    {
        var result = await new ElasticSearchMappingDriftHealthCheck(new[] { Probe(Clean("a")), Probe(Clean("b")) }).CheckAsync();

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("2 store(s) checked");
    }

    [Fact]
    public async Task Drift_is_Degraded_and_names_each_field()
    {
        var drifted = new MappingDriftReport("orders", true, true, new[]
        {
            new MappingDrift("orders", "code", MappingDriftKind.TypeMismatch, "keyword", "text"),
        });

        var result = await new ElasticSearchMappingDriftHealthCheck(new[] { Probe(Clean("a")), Probe(drifted) }).CheckAsync();

        result.Status.Should().Be(HealthStatus.Degraded, "drift needs a human, never a load-balancer eviction");
        result.Data!["drifted"].Should().Be(1);
        result.Data["drift"].Should().BeAssignableTo<IEnumerable<string>>().Which.Should().ContainSingle()
            .Which.Should().Contain("code").And.Contain("keyword").And.Contain("text");
    }

    [Fact]
    public async Task A_store_that_could_not_be_asked_is_not_Healthy()
    {
        var unsupported = new MappingDriftReport("x", false, false, Array.Empty<MappingDrift>(), "no client");

        var result = await new ElasticSearchMappingDriftHealthCheck(new[] { Probe(unsupported) }).CheckAsync();

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("could not be determined").And.Contain("no client");
    }

    [Fact]
    public async Task An_index_not_created_yet_is_Healthy_and_says_it_was_not_checked()
    {
        var absent = new MappingDriftReport("later", true, false, Array.Empty<MappingDrift>());

        var result = await new ElasticSearchMappingDriftHealthCheck(new[] { Probe(Clean("a")), Probe(absent) }).CheckAsync();

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("1 of 2").And.Contain("not created yet");
    }

    [Fact]
    public async Task A_probe_that_throws_is_Unhealthy()
    {
        Func<CancellationToken, Task<MappingDriftReport>> broken = _ => throw new InvalidOperationException("cluster down");

        var result = await new ElasticSearchMappingDriftHealthCheck(new[] { broken }).CheckAsync();

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("cluster down");
    }
}

public class LiveDoc : AbstractModel
{
    [Nest.Keyword(Name = "code")]
    public string Code { get; set; } = string.Empty;
}

/// <summary>TASK-624, live: the check over a real store whose index was mapped for an older model.</summary>
public sealed class ElasticSearchMappingDriftHealthCheckLiveTests : IDisposable
{
    private static readonly HttpClient Http = new();
    private static string? Url => Environment.GetEnvironmentVariable("BIRKO_ES_URL");
    private readonly ITestOutputHelper _output;
    private readonly string _prefix = "birko624" + Guid.NewGuid().ToString("N");

    public ElasticSearchMappingDriftHealthCheckLiveTests(ITestOutputHelper output) => _output = output;

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(Url)) return;
        try { Http.DeleteAsync(Url.TrimEnd('/') + "/" + _prefix + "*").GetAwaiter().GetResult(); } catch { }
    }

    [Fact]
    public async Task A_store_over_an_index_mapped_for_an_older_model_is_Degraded()
    {
        if (string.IsNullOrWhiteSpace(Url))
        {
            _output.WriteLine("SKIPPED: no live Elasticsearch. Set BIRKO_ES_URL.");
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
                throw new InvalidOperationException("BIRKO_REQUIRE_LIVE is set but BIRKO_ES_URL is not.");
            return;
        }
        var store = new AsyncElasticSearchStore<LiveDoc>();
        store.SetSettings(new Settings { Location = Url, Name = _prefix });
        const string oldMapping = @"{""mappings"":{""properties"":{""guid"":{""type"":""keyword""},""code"":{""type"":""text""}}}}";
        (await Http.PutAsync($"{Url.TrimEnd('/')}/{store.GetIndexName()}", new StringContent(oldMapping, Encoding.UTF8, "application/json")))
            .EnsureSuccessStatusCode();

        var result = await new ElasticSearchMappingDriftHealthCheck(new Func<CancellationToken, Task<MappingDriftReport>>[]
        {
            ct => store.DetectMappingDriftAsync(ct),
        }).CheckAsync();

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Data!["drift"].Should().BeAssignableTo<IEnumerable<string>>().Which.Should().Contain(l => l.Contains("code"));
    }
}
