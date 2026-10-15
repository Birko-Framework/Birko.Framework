using System;
using System.Collections.Generic;
using System.Net.Http;
using Birko.Data.ElasticSearch.Stores;
using Birko.Data.Models;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.ElasticSearch.Tests;

public class UpdateMissDoc : AbstractModel
{
    public string? Name { get; set; }
}

/// <summary>
/// Live-server plumbing for the Elasticsearch contract classes. Gated on <c>BIRKO_ES_URL</c> (no CI job runs it yet;
/// locally: <c>docker run -p 9200:9200 -e discovery.type=single-node -e xpack.security.enabled=false
/// docker.elastic.co/elasticsearch/elasticsearch:7.17.25</c>). Each store gets its own index prefix, deleted after.
/// </summary>
internal sealed class ElasticLive : IDisposable
{
    private static readonly HttpClient Http = new();
    private readonly ITestOutputHelper _output;
    private readonly List<string> _prefixes = new();
    private static string? Url => Environment.GetEnvironmentVariable("BIRKO_ES_URL");

    public ElasticLive(ITestOutputHelper output) => _output = output;

    public bool Ready()
    {
        if (!string.IsNullOrWhiteSpace(Url)) return true;
        const string message = "SKIPPED: no live Elasticsearch. Set BIRKO_ES_URL (e.g. http://localhost:9200); "
                             + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
            throw new InvalidOperationException(message);
        return false;
    }

    public Settings NewSettings()
    {
        var prefix = "birko555" + Guid.NewGuid().ToString("N");
        _prefixes.Add(prefix);
        return new Settings { Location = Url!, Name = prefix };
    }

    /// <summary>Elasticsearch is near-real-time: a write is searchable only after a refresh.</summary>
    public static void Refresh() => Http.PostAsync(Url!.TrimEnd('/') + "/_refresh", null).GetAwaiter().GetResult().EnsureSuccessStatusCode();

    public void Dispose()
    {
        foreach (var prefix in _prefixes)
        {
            try { Http.DeleteAsync(Url!.TrimEnd('/') + "/" + prefix + "*").GetAwaiter().GetResult(); } catch { }
        }
    }
}

/// <summary>TASK-555's cross-provider update-miss contract on <see cref="ElasticSearchStore{T}"/>, live.</summary>
public sealed class ElasticUpdateMissContractTests : StoreContract<UpdateMissDoc>, IDisposable
{
    private readonly ElasticLive _live;
    public ElasticUpdateMissContractTests(ITestOutputHelper output) => _live = new ElasticLive(output);
    protected override bool Ready() => _live.Ready();

    protected override IBulkStore<UpdateMissDoc> NewStore()
    {
        var store = new ElasticSearchStore<UpdateMissDoc>();
        store.SetSettings(_live.NewSettings());
        return store;
    }

    protected override void Settle(IBulkStore<UpdateMissDoc> store) => ElasticLive.Refresh();
    protected override UpdateMissDoc NewEntity(string name) => new() { Name = name };
    protected override string NameOf(UpdateMissDoc entity) => entity.Name ?? "";
    protected override void Rename(UpdateMissDoc entity, string name) => entity.Name = name;
    public void Dispose() => _live.Dispose();
}

/// <summary>The async twin, on <see cref="AsyncElasticSearchStore{T}"/>.</summary>
public sealed class AsyncElasticUpdateMissContractTests : AsyncStoreContract<UpdateMissDoc>, IDisposable
{
    private readonly ElasticLive _live;
    public AsyncElasticUpdateMissContractTests(ITestOutputHelper output) => _live = new ElasticLive(output);
    protected override bool Ready() => _live.Ready();

    protected override IAsyncBulkStore<UpdateMissDoc> NewStore()
    {
        var store = new AsyncElasticSearchStore<UpdateMissDoc>();
        store.SetSettings(_live.NewSettings());
        return store;
    }

    protected override void Settle(IAsyncBulkStore<UpdateMissDoc> store) => ElasticLive.Refresh();
    protected override UpdateMissDoc NewEntity(string name) => new() { Name = name };
    protected override string NameOf(UpdateMissDoc entity) => entity.Name ?? "";
    protected override void Rename(UpdateMissDoc entity, string name) => entity.Name = name;
    public void Dispose() => _live.Dispose();
}
