using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.PostgreSQL.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.SQL.PostgreSQL.Tests;

[Table("T886StringOrderRows")]
public class StringOrderRow : AbstractModel
{
    public string? Kind { get; set; }
    public string? Name { get; set; }
    public int N { get; set; }
}

/// <summary>TASK-907's aggregate string order contract (<c>tests/Shared/StoreContract</c>) on the PostgreSQL stores, live. The
/// table is dropped for each store, so each case starts from no rows.</summary>
public sealed class PostgreSQLAggregateStringOrderContractTests : AggregateStringOrderContract<StringOrderRow>
{
    private readonly PostgreSQLLive _live;
    public PostgreSQLAggregateStringOrderContractTests(ITestOutputHelper output) => _live = new PostgreSQLLive(output);
    protected override bool Ready() => _live.Ready();

    protected override IAsyncBulkStore<StringOrderRow> NewAsyncStore()
    {
        var store = new AsyncPostgreSQLStore<StringOrderRow>();
        store.SetSettings(_live.Settings());
        store.DestroyAsync().GetAwaiter().GetResult();
        return store;
    }

    protected override IBulkStore<StringOrderRow> NewSyncStore()
    {
        var store = new PostgreSQLStore<StringOrderRow>();
        store.SetSettings(_live.Settings());
        store.Destroy();
        return store;
    }
}
