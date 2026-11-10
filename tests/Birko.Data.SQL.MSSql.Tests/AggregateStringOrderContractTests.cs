using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.MSSql.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MSSql.Tests;

[Table("T886StringOrderRows")]
public class StringOrderRow : AbstractModel
{
    public string? Kind { get; set; }
    public string? Name { get; set; }
    public int N { get; set; }
}

/// <summary>TASK-907's aggregate string order contract (<c>tests/Shared/StoreContract</c>) on the SQL Server stores, live. The
/// table is dropped for each store, so each case starts from no rows.</summary>
public sealed class SQLServerAggregateStringOrderContractTests : AggregateStringOrderContract<StringOrderRow>
{
    private readonly SQLServerLive _live;
    public SQLServerAggregateStringOrderContractTests(ITestOutputHelper output) => _live = new SQLServerLive(output);
    protected override bool Ready() => _live.Ready();

    protected override IAsyncBulkStore<StringOrderRow> NewAsyncStore()
    {
        var store = new AsyncMSSqlStore<StringOrderRow>();
        store.SetSettings(_live.Settings());
        store.DestroyAsync().GetAwaiter().GetResult();
        return store;
    }

    protected override IBulkStore<StringOrderRow> NewSyncStore()
    {
        var store = new MSSqlStore<StringOrderRow>();
        store.SetSettings(_live.Settings());
        store.Destroy();
        return store;
    }
}
