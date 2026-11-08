using Birko.Data.SQL.MSSql.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MSSql.Tests;

/// <summary>TASK-860's call-and-cast contract on <see cref="AsyncMSSqlStore{T}"/>, live.</summary>
public sealed class SQLServerCallCastFilterContractTests : CallCastFilterContract
{
    private readonly SQLServerLive _live;
    public SQLServerCallCastFilterContractTests(ITestOutputHelper output) => _live = new SQLServerLive(output);
    protected override bool Ready() => _live.Ready();

    protected override IAsyncBulkStore<CallCastRow> NewStore()
    {
        var store = new AsyncMSSqlStore<CallCastRow>();
        store.SetSettings(_live.Settings());
        store.DestroyAsync().GetAwaiter().GetResult();
        return store;
    }
}
