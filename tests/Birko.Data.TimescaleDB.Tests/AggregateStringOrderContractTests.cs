using System;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.TimescaleDB.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.TimescaleDB.Tests;

[Table("T886StringOrderRows")]
public class StringOrderRow : AbstractModel
{
    public string? Kind { get; set; }
    public string? Name { get; set; }
    public int N { get; set; }
    public DateTime Ts { get; set; } = DateTime.UtcNow;
}

/// <summary>TASK-907's aggregate string order contract (<c>tests/Shared/StoreContract</c>) on the TimescaleDB stores, live. The
/// table is dropped for each store, so each case starts from no rows.</summary>
public sealed class TimescaleDBAggregateStringOrderContractTests : AggregateStringOrderContract<StringOrderRow>
{
    private readonly TimescaleDBLive _live;
    public TimescaleDBAggregateStringOrderContractTests(ITestOutputHelper output) => _live = new TimescaleDBLive(output);
    protected override bool Ready() => _live.Ready();

    protected override IAsyncBulkStore<StringOrderRow> NewAsyncStore()
    {
        var store = new AsyncTimescaleDBStore<StringOrderRow>();
        store.SetSettings(_live.Settings());
        store.DestroyAsync().GetAwaiter().GetResult();
        return store;
    }

    protected override IBulkStore<StringOrderRow> NewSyncStore()
    {
        var store = new TimescaleDBStore<StringOrderRow>();
        store.SetSettings(_live.Settings());
        store.Destroy();
        return store;
    }
}
