using Birko.Data.SQL.MySQL.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MySQL.Tests;

/// <summary>TASK-860's call-and-cast contract on <see cref="AsyncMySQLStore{T}"/>, live.</summary>
public sealed class MySQLCallCastFilterContractTests : CallCastFilterContract
{
    private readonly MySQLLive _live;
    public MySQLCallCastFilterContractTests(ITestOutputHelper output) => _live = new MySQLLive(output);
    protected override bool Ready() => _live.Ready();

    protected override IAsyncBulkStore<CallCastRow> NewStore()
    {
        var store = new AsyncMySQLStore<CallCastRow>();
        store.SetSettings(_live.Settings());
        store.DestroyAsync().GetAwaiter().GetResult();
        return store;
    }
}
