using Birko.Data.SQL.MySQL.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MySQL.Tests;

/// <summary>TASK-861's always-false-group contract on <see cref="MySQLStore{T}"/> and <see cref="AsyncMySQLStore{T}"/>, live.</summary>
public sealed class MySQLAlwaysFalseGroupContractTests : AlwaysFalseGroupContract
{
    private readonly MySQLLive _live;
    public MySQLAlwaysFalseGroupContractTests(ITestOutputHelper output) => _live = new MySQLLive(output);
    protected override bool Ready() => _live.Ready();

    protected override IAsyncBulkStore<AlwaysFalseRow> NewAsyncStore()
    {
        var store = new AsyncMySQLStore<AlwaysFalseRow>();
        store.SetSettings(_live.Settings());
        store.DestroyAsync().GetAwaiter().GetResult();
        return store;
    }

    protected override IBulkStore<AlwaysFalseRow> NewSyncStore()
    {
        var store = new MySQLStore<AlwaysFalseRow>();
        store.SetSettings(_live.Settings());
        store.Destroy();
        return store;
    }
}
