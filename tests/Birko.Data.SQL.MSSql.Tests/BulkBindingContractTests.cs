using Birko.Data.SQL.MSSql.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MSSql.Tests;

/// <summary>TASK-1007's bulk-binding contract on <see cref="MSSqlStore{T}"/> and <see cref="AsyncMSSqlStore{T}"/>, live. Each store's table is
/// dropped first.</summary>
public sealed class SQLServerBulkBindingContractTests : BulkBindingContract
{
    private readonly SQLServerLive _live;
    public SQLServerBulkBindingContractTests(ITestOutputHelper output) => _live = new SQLServerLive(output);
    protected override bool Ready() => _live.Ready();

    private IAsyncBulkStore<T> Async<T>() where T : Birko.Data.Models.AbstractModel
    {
        var store = new AsyncMSSqlStore<T>();
        store.SetSettings(_live.Settings());
        store.DestroyAsync().GetAwaiter().GetResult();
        return store;
    }

    private IBulkStore<T> Sync<T>() where T : Birko.Data.Models.AbstractModel
    {
        var store = new MSSqlStore<T>();
        store.SetSettings(_live.Settings());
        store.Destroy();
        return store;
    }

    protected override IAsyncBulkStore<BulkBindRow> NewAsyncRowStore() => Async<BulkBindRow>();
    protected override IBulkStore<BulkBindRow> NewSyncRowStore() => Sync<BulkBindRow>();
    protected override IAsyncBulkStore<BulkBindKeyed> NewAsyncKeyedStore() => Async<BulkBindKeyed>();
    protected override IBulkStore<BulkBindKeyed> NewSyncKeyedStore() => Sync<BulkBindKeyed>();
}
