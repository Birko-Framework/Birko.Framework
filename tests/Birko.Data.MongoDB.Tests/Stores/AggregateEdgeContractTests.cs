using System;
using Birko.Data.MongoDB.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.MongoDB.Tests.Stores;

public class AggregateEdgeDoc : Birko.Data.MongoDB.Models.MongoDBModel
{
    public string? Code { get; set; }
    public string? Kind { get; set; }
    public int N { get; set; }
    public int? Score { get; set; }
    public decimal? Price { get; set; }
}

/// <summary>TASK-886's aggregate edge contract (<c>tests/Shared/StoreContract</c>) on the MongoDB stores, live.</summary>
public sealed class MongoAggregateEdgeContractTests : AggregateEdgeContract<AggregateEdgeDoc>, IDisposable
{
    private readonly MongoLive _live;
    public MongoAggregateEdgeContractTests(ITestOutputHelper output) => _live = new MongoLive(output);
    protected override bool Ready() => _live.Ready();

    protected override IAsyncBulkStore<AggregateEdgeDoc> NewAsyncStore()
    {
        var store = new AsyncMongoDBStore<AggregateEdgeDoc>();
        store.SetSettings(_live.NewSettings());
        return store;
    }

    protected override IBulkStore<AggregateEdgeDoc> NewSyncStore()
    {
        var store = new MongoDBStore<AggregateEdgeDoc>();
        store.SetSettings(_live.NewSettings());
        return store;
    }

    public void Dispose() => _live.Dispose();
}
