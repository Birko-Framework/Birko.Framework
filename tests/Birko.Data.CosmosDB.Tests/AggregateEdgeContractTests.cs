using System;
using Birko.Data.CosmosDB.Stores;
using Birko.Data.Models;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.CosmosDB.Tests;

public class AggregateEdgeDoc : AbstractModel
{
    public string? Code { get; set; }
    public string? Kind { get; set; }
    public int N { get; set; }
    public int? Score { get; set; }
    public decimal? Price { get; set; }
}

/// <summary>TASK-886's aggregate edge contract (<c>tests/Shared/StoreContract</c>) on the Cosmos DB stores, live.</summary>
public sealed class CosmosAggregateEdgeContractTests : AggregateEdgeContract<AggregateEdgeDoc>, IDisposable
{
    private readonly CosmosLive _live;
    public CosmosAggregateEdgeContractTests(ITestOutputHelper output) => _live = new CosmosLive(output);
    protected override bool Ready() => _live.Ready();

    protected override IAsyncBulkStore<AggregateEdgeDoc> NewAsyncStore()
    {
        var (conn, db, settings) = _live.Next();
        return new AsyncCosmosDBStore<AggregateEdgeDoc>(conn, db, null, settings);
    }

    protected override IBulkStore<AggregateEdgeDoc> NewSyncStore()
    {
        var (conn, db, settings) = _live.Next();
        return new CosmosDBStore<AggregateEdgeDoc>(conn, db, null, settings);
    }

    public void Dispose() => _live.Dispose();
}
