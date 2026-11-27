using System.Net.Http;
using Birko.Data.ElasticSearch.Stores;
using Birko.Data.EventSourcing.ElasticSearch;
using Birko.Data.EventSourcing.Events;
using Birko.Data.EventSourcing.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.EventSourcing;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.EventSourcing.ElasticSearch.Tests;

/// <summary>
/// TASK-995 — the shared event store contract on <see cref="ElasticSearchEventStore"/>, live, obtained as an application
/// does: <c>factory.GetEventStore(o => o.AddElasticSearch())</c>. A new factory per writer, as a second process has.
/// </summary>
/// <remarks>Gated on <c>BIRKO_ES_URL</c>; set <c>BIRKO_REQUIRE_LIVE</c> to make its absence a failure.</remarks>
public sealed class ElasticSearchEventStoreContractTests : EventStoreContract, IDisposable
{
    private static readonly HttpClient Http = new();
    private static string? Url => Environment.GetEnvironmentVariable("BIRKO_ES_URL");
    private readonly ITestOutputHelper _output;
    private readonly string _prefix = "birko995" + Guid.NewGuid().ToString("N");

    public ElasticSearchEventStoreContractTests(ITestOutputHelper output) => _output = output;

    protected override bool Ready()
    {
        if (!string.IsNullOrWhiteSpace(Url)) return true;
        const string message = "SKIPPED: no live Elasticsearch. Set BIRKO_ES_URL (e.g. http://localhost:9200); "
                             + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
            throw new InvalidOperationException(message);
        return false;
    }

    private IStoreFactory NewFactory()
        => Birko.Data.Stores.StoreFactory.Create("ElasticSearch", $"Url={Url};IndexPrefix={_prefix}", p => p.AddElasticSearch());

    protected override Task<IAsyncEventStore> NewStoreAsync() => Task.FromResult(NewFactory().GetEventStore(o => o.AddElasticSearch()));

    [Fact]
    public void An_Elasticsearch_factory_answers_the_Elasticsearch_event_store()
    {
        if (!Ready()) return;
        NewFactory().GetEventStore(o => o.AddElasticSearch()).Should().BeOfType<ElasticSearchEventStore>();
        NewFactory().GetEventStore().Should().BeOfType<AsyncStoreEventStore>("without the override the default is answered");
    }

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(Url)) return;
        try { Http.DeleteAsync(Url!.TrimEnd('/') + "/" + _prefix + "*").GetAwaiter().GetResult(); } catch { }
    }
}
