using Birko.Data.SQL.MSSql.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MSSql.Tests;

/// <summary>TASK-866's collapsed-group contract on <see cref="MSSqlStore{T}"/> and <see cref="AsyncMSSqlStore{T}"/>, live.</summary>
public sealed class SQLServerCollapsedGroupContractTests : CollapsedGroupContract
{
    private readonly SQLServerLive _live;
    public SQLServerCollapsedGroupContractTests(ITestOutputHelper output) => _live = new SQLServerLive(output);
    protected override bool Ready() => _live.Ready();

    protected override IAsyncBulkStore<CollapsedGroupRow> NewAsyncStore()
    {
        var store = new AsyncMSSqlStore<CollapsedGroupRow>();
        store.SetSettings(_live.Settings());
        store.DestroyAsync().GetAwaiter().GetResult();
        return store;
    }

    protected override IBulkStore<CollapsedGroupRow> NewSyncStore()
    {
        var store = new MSSqlStore<CollapsedGroupRow>();
        store.SetSettings(_live.Settings());
        store.Destroy();
        return store;
    }
}
