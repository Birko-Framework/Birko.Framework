using System;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.TimescaleDB.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.TimescaleDB.Tests;

[Table("T886AggregateEdgeRows")]
public class AggregateEdgeRow : AbstractModel
{
    public string? Code { get; set; }
    public string? Kind { get; set; }
    public int N { get; set; }
    public int? Score { get; set; }
    public decimal? Price { get; set; }
    public DateTime Ts { get; set; } = DateTime.UtcNow;
}

/// <summary>TASK-886's aggregate edge contract (<c>tests/Shared/StoreContract</c>) on the TimescaleDB stores, live. The
/// table is dropped for each store, so each case starts from no rows.</summary>
public sealed class TimescaleDBAggregateEdgeContractTests : AggregateEdgeContract<AggregateEdgeRow>
{
    private readonly TimescaleDBLive _live;
    public TimescaleDBAggregateEdgeContractTests(ITestOutputHelper output) => _live = new TimescaleDBLive(output);
    protected override bool Ready() => _live.Ready();

    protected override IAsyncBulkStore<AggregateEdgeRow> NewAsyncStore()
    {
        var store = new AsyncTimescaleDBStore<AggregateEdgeRow>();
        store.SetSettings(_live.Settings());
        store.DestroyAsync().GetAwaiter().GetResult();
        return store;
    }

    protected override IBulkStore<AggregateEdgeRow> NewSyncStore()
    {
        var store = new TimescaleDBStore<AggregateEdgeRow>();
        store.SetSettings(_live.Settings());
        store.Destroy();
        return store;
    }
}
