using Birko.Data.SQL.MSSql.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MSSql.Tests;

/// <summary>TASK-857's math-filter contract on <see cref="AsyncMSSqlStore{T}"/>, live.</summary>
public sealed class SQLServerMathFilterContractTests : MathFilterContract
{
    private readonly SQLServerLive _live;
    public SQLServerMathFilterContractTests(ITestOutputHelper output) => _live = new SQLServerLive(output);
    protected override bool Ready() => _live.Ready();

    protected override IAsyncBulkStore<MathFilterRow> NewStore()
    {
        var store = new AsyncMSSqlStore<MathFilterRow>();
        store.SetSettings(_live.Settings());
        store.DestroyAsync().GetAwaiter().GetResult();
        return store;
    }
}
