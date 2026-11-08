using Birko.Data.SQL.MySQL.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MySQL.Tests;

/// <summary>TASK-863's boolean-against-a-constant contract on <see cref="AsyncMySQLStore{T}"/>, live.</summary>
public sealed class MySQLBoolComparisonContractTests : BoolComparisonContract
{
    private readonly MySQLLive _live;
    public MySQLBoolComparisonContractTests(ITestOutputHelper output) => _live = new MySQLLive(output);
    protected override bool Ready() => _live.Ready();

    protected override IAsyncBulkStore<BoolComparisonRow> NewStore()
    {
        var store = new AsyncMySQLStore<BoolComparisonRow>();
        store.SetSettings(_live.Settings());
        store.DestroyAsync().GetAwaiter().GetResult();
        return store;
    }
}
