using System;
using Birko.Data.ElasticSearch.Stores;
using Birko.Data.Models;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.ElasticSearch.Tests;

public class AggregateEdgeDoc : AbstractModel
{
    public string? Code { get; set; }
    public string? Kind { get; set; }
    public int N { get; set; }
    public int? Score { get; set; }
    public decimal? Price { get; set; }
}

/// <summary>TASK-886's aggregate edge contract (<c>tests/Shared/StoreContract</c>) on the Elasticsearch stores, live.</summary>
public sealed class ElasticAggregateEdgeContractTests : AggregateEdgeContract<AggregateEdgeDoc>, IDisposable
{
    private readonly ElasticLive _live;
    public ElasticAggregateEdgeContractTests(ITestOutputHelper output) => _live = new ElasticLive(output);
    protected override bool Ready() => _live.Ready();
    protected override void Settle() => ElasticLive.Refresh();

    protected override IAsyncBulkStore<AggregateEdgeDoc> NewAsyncStore()
    {
        var store = new AsyncElasticSearchStore<AggregateEdgeDoc>();
        store.SetSettings(_live.NewSettings());
        return store;
    }

    protected override IBulkStore<AggregateEdgeDoc> NewSyncStore()
    {
        var store = new ElasticSearchStore<AggregateEdgeDoc>();
        store.SetSettings(_live.NewSettings());
        return store;
    }

    public void Dispose() => _live.Dispose();
}
