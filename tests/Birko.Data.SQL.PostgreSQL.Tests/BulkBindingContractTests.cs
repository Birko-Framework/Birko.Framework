using Birko.Data.SQL.PostgreSQL.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.SQL.PostgreSQL.Tests;

/// <summary>TASK-1007's bulk-binding contract on <see cref="PostgreSQLStore{T}"/> and <see cref="AsyncPostgreSQLStore{T}"/>, live. Each store's table is
/// dropped first.</summary>
public sealed class PostgreSQLBulkBindingContractTests : BulkBindingContract
{
    private readonly PostgreSQLLive _live;
    public PostgreSQLBulkBindingContractTests(ITestOutputHelper output) => _live = new PostgreSQLLive(output);
    protected override bool Ready() => _live.Ready();

    private IAsyncBulkStore<T> Async<T>() where T : Birko.Data.Models.AbstractModel
    {
        var store = new AsyncPostgreSQLStore<T>();
        store.SetSettings(_live.Settings());
        store.DestroyAsync().GetAwaiter().GetResult();
        return store;
    }

    private IBulkStore<T> Sync<T>() where T : Birko.Data.Models.AbstractModel
    {
        var store = new PostgreSQLStore<T>();
        store.SetSettings(_live.Settings());
        store.Destroy();
        return store;
    }

    protected override IAsyncBulkStore<BulkBindRow> NewAsyncRowStore() => Async<BulkBindRow>();
    protected override IBulkStore<BulkBindRow> NewSyncRowStore() => Sync<BulkBindRow>();
    protected override IAsyncBulkStore<BulkBindKeyed> NewAsyncKeyedStore() => Async<BulkBindKeyed>();
    protected override IBulkStore<BulkBindKeyed> NewSyncKeyedStore() => Sync<BulkBindKeyed>();
}
