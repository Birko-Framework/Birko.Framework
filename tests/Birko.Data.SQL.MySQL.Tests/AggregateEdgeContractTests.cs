using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.MySQL.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MySQL.Tests;

[Table("T886AggregateEdgeRows")]
public class AggregateEdgeRow : AbstractModel
{
    public string? Code { get; set; }
    public string? Kind { get; set; }
    public int N { get; set; }
    public int? Score { get; set; }
    public decimal? Price { get; set; }
}

/// <summary>TASK-886's aggregate edge contract (<c>tests/Shared/StoreContract</c>) on the MySQL stores, live. The table
/// is dropped for each store, so each case starts from no rows.</summary>
public sealed class MySQLAggregateEdgeContractTests : AggregateEdgeContract<AggregateEdgeRow>
{
    private readonly MySQLLive _live;
    public MySQLAggregateEdgeContractTests(ITestOutputHelper output) => _live = new MySQLLive(output);
    protected override bool Ready() => _live.Ready();

    protected override IAsyncBulkStore<AggregateEdgeRow> NewAsyncStore()
    {
        var store = new AsyncMySQLStore<AggregateEdgeRow>();
        store.SetSettings(_live.Settings());
        store.DestroyAsync().GetAwaiter().GetResult();
        return store;
    }

    protected override IBulkStore<AggregateEdgeRow> NewSyncStore()
    {
        var store = new MySQLStore<AggregateEdgeRow>();
        store.SetSettings(_live.Settings());
        store.Destroy();
        return store;
    }
}
