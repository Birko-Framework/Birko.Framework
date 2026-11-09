using Birko.Data.SQL.MySQL.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MySQL.Tests;

/// <summary>TASK-873's string-equality contract on <see cref="AsyncMySQLStore{T}"/>, live.</summary>
public sealed class MySQLStringEqualityContractTests : StringEqualityContract
{
    private readonly MySQLLive _live;
    public MySQLStringEqualityContractTests(ITestOutputHelper output) => _live = new MySQLLive(output);
    protected override bool Ready() => _live.Ready();

    protected override IAsyncBulkStore<StringEqualityRow> NewStore()
    {
        var store = new AsyncMySQLStore<StringEqualityRow>();
        store.SetSettings(_live.Settings());
        store.DestroyAsync().GetAwaiter().GetResult();
        return store;
    }
}
