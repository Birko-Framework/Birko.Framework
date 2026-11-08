using Birko.Data.SQL.PostgreSQL.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.SQL.PostgreSQL.Tests;

/// <summary>TASK-860's call-and-cast contract on <see cref="AsyncPostgreSQLStore{T}"/>, live.</summary>
public sealed class PostgreSQLCallCastFilterContractTests : CallCastFilterContract
{
    private readonly PostgreSQLLive _live;
    public PostgreSQLCallCastFilterContractTests(ITestOutputHelper output) => _live = new PostgreSQLLive(output);
    protected override bool Ready() => _live.Ready();

    protected override IAsyncBulkStore<CallCastRow> NewStore()
    {
        var store = new AsyncPostgreSQLStore<CallCastRow>();
        store.SetSettings(_live.Settings());
        store.DestroyAsync().GetAwaiter().GetResult();
        return store;
    }
}
