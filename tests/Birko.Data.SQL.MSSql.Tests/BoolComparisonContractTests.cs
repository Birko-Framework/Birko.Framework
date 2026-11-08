using Birko.Data.SQL.MSSql.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MSSql.Tests;

/// <summary>TASK-863's boolean-against-a-constant contract on <see cref="AsyncMSSqlStore{T}"/>, live.</summary>
public sealed class SQLServerBoolComparisonContractTests : BoolComparisonContract
{
    private readonly SQLServerLive _live;
    public SQLServerBoolComparisonContractTests(ITestOutputHelper output) => _live = new SQLServerLive(output);
    protected override bool Ready() => _live.Ready();

    protected override IAsyncBulkStore<BoolComparisonRow> NewStore()
    {
        var store = new AsyncMSSqlStore<BoolComparisonRow>();
        store.SetSettings(_live.Settings());
        store.DestroyAsync().GetAwaiter().GetResult();
        return store;
    }
}
