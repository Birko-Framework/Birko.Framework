using Birko.Data.SQL.PostgreSQL.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.SQL.PostgreSQL.Tests;

/// <summary>TASK-859's nullable-filter contract on <see cref="AsyncPostgreSQLStore{T}"/>, live.</summary>
public sealed class PostgreSQLNullFilterContractTests : NullFilterContract
{
    private readonly PostgreSQLLive _live;
    public PostgreSQLNullFilterContractTests(ITestOutputHelper output) => _live = new PostgreSQLLive(output);
    protected override bool Ready() => _live.Ready();

    protected override IAsyncBulkStore<NullFilterRow> NewStore()
    {
        var store = new AsyncPostgreSQLStore<NullFilterRow>();
        store.SetSettings(_live.Settings());
        store.DestroyAsync().GetAwaiter().GetResult();
        return store;
    }
}
