using Birko.Data.SQL.MSSql.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MSSql.Tests;

/// <summary>TASK-861's always-false-group contract on <see cref="MSSqlStore{T}"/> and <see cref="AsyncMSSqlStore{T}"/>, live.</summary>
public sealed class SQLServerAlwaysFalseGroupContractTests : AlwaysFalseGroupContract
{
    private readonly SQLServerLive _live;
    public SQLServerAlwaysFalseGroupContractTests(ITestOutputHelper output) => _live = new SQLServerLive(output);
    protected override bool Ready() => _live.Ready();

    protected override IAsyncBulkStore<AlwaysFalseRow> NewAsyncStore()
    {
        var store = new AsyncMSSqlStore<AlwaysFalseRow>();
        store.SetSettings(_live.Settings());
        store.DestroyAsync().GetAwaiter().GetResult();
        return store;
    }

    protected override IBulkStore<AlwaysFalseRow> NewSyncStore()
    {
        var store = new MSSqlStore<AlwaysFalseRow>();
        store.SetSettings(_live.Settings());
        store.Destroy();
        return store;
    }
}
