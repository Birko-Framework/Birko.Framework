using Birko.Data.SQL.PostgreSQL.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.SQL.PostgreSQL.Tests;

/// <summary>TASK-863's boolean-against-a-constant contract on <see cref="AsyncPostgreSQLStore{T}"/>, live.</summary>
public sealed class PostgreSQLBoolComparisonContractTests : BoolComparisonContract
{
    private readonly PostgreSQLLive _live;
    public PostgreSQLBoolComparisonContractTests(ITestOutputHelper output) => _live = new PostgreSQLLive(output);
    protected override bool Ready() => _live.Ready();

    protected override IAsyncBulkStore<BoolComparisonRow> NewStore()
    {
        var store = new AsyncPostgreSQLStore<BoolComparisonRow>();
        store.SetSettings(_live.Settings());
        store.DestroyAsync().GetAwaiter().GetResult();
        return store;
    }
}
