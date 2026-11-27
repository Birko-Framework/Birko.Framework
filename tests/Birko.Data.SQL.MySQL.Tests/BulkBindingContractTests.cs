using Birko.Data.SQL.MySQL.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MySQL.Tests;

/// <summary>TASK-1007's bulk-binding contract on <see cref="MySQLStore{T}"/> and <see cref="AsyncMySQLStore{T}"/>, live. Each store's table is
/// dropped first.</summary>
public sealed class MySQLBulkBindingContractTests : BulkBindingContract
{
    private readonly MySQLLive _live;
    public MySQLBulkBindingContractTests(ITestOutputHelper output) => _live = new MySQLLive(output);
    protected override bool Ready() => _live.Ready();

    private IAsyncBulkStore<T> Async<T>() where T : Birko.Data.Models.AbstractModel
    {
        var store = new AsyncMySQLStore<T>();
        store.SetSettings(_live.Settings());
        store.DestroyAsync().GetAwaiter().GetResult();
        return store;
    }

    private IBulkStore<T> Sync<T>() where T : Birko.Data.Models.AbstractModel
    {
        var store = new MySQLStore<T>();
        store.SetSettings(_live.Settings());
        store.Destroy();
        return store;
    }

    protected override IAsyncBulkStore<BulkBindRow> NewAsyncRowStore() => Async<BulkBindRow>();
    protected override IBulkStore<BulkBindRow> NewSyncRowStore() => Sync<BulkBindRow>();
    protected override IAsyncBulkStore<BulkBindKeyed> NewAsyncKeyedStore() => Async<BulkBindKeyed>();
    protected override IBulkStore<BulkBindKeyed> NewSyncKeyedStore() => Sync<BulkBindKeyed>();
}
