using Birko.Data.SQL.MySQL.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MySQL.Tests;

/// <summary>TASK-866's collapsed-group contract on <see cref="MySQLStore{T}"/> and <see cref="AsyncMySQLStore{T}"/>, live.</summary>
public sealed class MySQLCollapsedGroupContractTests : CollapsedGroupContract
{
    private readonly MySQLLive _live;
    public MySQLCollapsedGroupContractTests(ITestOutputHelper output) => _live = new MySQLLive(output);
    protected override bool Ready() => _live.Ready();

    protected override IAsyncBulkStore<CollapsedGroupRow> NewAsyncStore()
    {
        var store = new AsyncMySQLStore<CollapsedGroupRow>();
        store.SetSettings(_live.Settings());
        store.DestroyAsync().GetAwaiter().GetResult();
        return store;
    }

    protected override IBulkStore<CollapsedGroupRow> NewSyncStore()
    {
        var store = new MySQLStore<CollapsedGroupRow>();
        store.SetSettings(_live.Settings());
        store.Destroy();
        return store;
    }
}
