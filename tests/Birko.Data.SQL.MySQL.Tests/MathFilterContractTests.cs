using Birko.Data.SQL.MySQL.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MySQL.Tests;

/// <summary>TASK-857's math-filter contract on <see cref="AsyncMySQLStore{T}"/>, live.</summary>
public sealed class MySQLMathFilterContractTests : MathFilterContract
{
    private readonly MySQLLive _live;
    public MySQLMathFilterContractTests(ITestOutputHelper output) => _live = new MySQLLive(output);
    protected override bool Ready() => _live.Ready();

    protected override IAsyncBulkStore<MathFilterRow> NewStore()
    {
        var store = new AsyncMySQLStore<MathFilterRow>();
        store.SetSettings(_live.Settings());
        store.DestroyAsync().GetAwaiter().GetResult();
        return store;
    }
}
