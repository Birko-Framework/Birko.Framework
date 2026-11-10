using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.PostgreSQL.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.SQL.PostgreSQL.Tests;

[Table("T886AggregateEdgeRows")]
public class AggregateEdgeRow : AbstractModel
{
    public string? Code { get; set; }
    public string? Kind { get; set; }
    public int N { get; set; }
    public int? Score { get; set; }
    public decimal? Price { get; set; }
}

/// <summary>TASK-886's aggregate edge contract (<c>tests/Shared/StoreContract</c>) on the PostgreSQL stores, live. The
/// table is dropped for each store, so each case starts from no rows.</summary>
public sealed class PostgreSQLAggregateEdgeContractTests : AggregateEdgeContract<AggregateEdgeRow>
{
    private readonly PostgreSQLLive _live;
    public PostgreSQLAggregateEdgeContractTests(ITestOutputHelper output) => _live = new PostgreSQLLive(output);
    protected override bool Ready() => _live.Ready();

    protected override IAsyncBulkStore<AggregateEdgeRow> NewAsyncStore()
    {
        var store = new AsyncPostgreSQLStore<AggregateEdgeRow>();
        store.SetSettings(_live.Settings());
        store.DestroyAsync().GetAwaiter().GetResult();
        return store;
    }

    protected override IBulkStore<AggregateEdgeRow> NewSyncStore()
    {
        var store = new PostgreSQLStore<AggregateEdgeRow>();
        store.SetSettings(_live.Settings());
        store.Destroy();
        return store;
    }
}
