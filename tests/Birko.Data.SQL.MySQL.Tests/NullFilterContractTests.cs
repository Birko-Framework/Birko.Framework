using Birko.Data.SQL.MySQL.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MySQL.Tests;

/// <summary>TASK-859's nullable-filter contract on <see cref="AsyncMySQLStore{T}"/>, live.</summary>
public sealed class MySQLNullFilterContractTests : NullFilterContract
{
    private readonly MySQLLive _live;
    public MySQLNullFilterContractTests(ITestOutputHelper output) => _live = new MySQLLive(output);
    protected override bool Ready() => _live.Ready();

    protected override IAsyncBulkStore<NullFilterRow> NewStore()
    {
        var store = new AsyncMySQLStore<NullFilterRow>();
        store.SetSettings(_live.Settings());
        store.DestroyAsync().GetAwaiter().GetResult();
        return store;
    }
}
