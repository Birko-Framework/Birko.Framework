using System;
using Birko.Data.Models;
using Birko.Data.RavenDB.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.RavenDB.Tests.Stores;

public class AggregateEdgeDoc : AbstractModel
{
    public string? Code { get; set; }
    public string? Kind { get; set; }
    public int N { get; set; }
    public int? Score { get; set; }
    public decimal? Price { get; set; }
}

/// <summary>TASK-886's aggregate edge contract (<c>tests/Shared/StoreContract</c>) on the RavenDB stores, live.</summary>
public sealed class RavenAggregateEdgeContractTests : AggregateEdgeContract<AggregateEdgeDoc>, IDisposable
{
    private readonly RavenLive _live;
    public RavenAggregateEdgeContractTests(ITestOutputHelper output) => _live = new RavenLive(output);
    protected override bool Ready() => _live.Ready();

    protected override IAsyncBulkStore<AggregateEdgeDoc> NewAsyncStore() => _live.Track((url, db) => new AsyncRavenDBStore<AggregateEdgeDoc>(url, db));
    protected override IBulkStore<AggregateEdgeDoc> NewSyncStore() => _live.Track((url, db) => new RavenDBStore<AggregateEdgeDoc>(url, db));
    public void Dispose() => _live.Dispose();
}
