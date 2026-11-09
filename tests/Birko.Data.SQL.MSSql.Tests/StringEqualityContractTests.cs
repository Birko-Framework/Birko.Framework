using Birko.Data.SQL.MSSql.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MSSql.Tests;

/// <summary>TASK-873's string-equality contract on <see cref="AsyncMSSqlStore{T}"/>, live.</summary>
public sealed class SQLServerStringEqualityContractTests : StringEqualityContract
{
    private readonly SQLServerLive _live;
    public SQLServerStringEqualityContractTests(ITestOutputHelper output) => _live = new SQLServerLive(output);
    protected override bool Ready() => _live.Ready();

    protected override IAsyncBulkStore<StringEqualityRow> NewStore()
    {
        var store = new AsyncMSSqlStore<StringEqualityRow>();
        store.SetSettings(_live.Settings());
        store.DestroyAsync().GetAwaiter().GetResult();
        return store;
    }
}
