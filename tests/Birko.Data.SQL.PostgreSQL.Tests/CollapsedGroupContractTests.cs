using Birko.Data.SQL.PostgreSQL.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.SQL.PostgreSQL.Tests;

/// <summary>TASK-866's collapsed-group contract on <see cref="PostgreSQLStore{T}"/> and <see cref="AsyncPostgreSQLStore{T}"/>, live.</summary>
public sealed class PostgreSQLCollapsedGroupContractTests : CollapsedGroupContract
{
    private readonly PostgreSQLLive _live;
    public PostgreSQLCollapsedGroupContractTests(ITestOutputHelper output) => _live = new PostgreSQLLive(output);
    protected override bool Ready() => _live.Ready();

    protected override IAsyncBulkStore<CollapsedGroupRow> NewAsyncStore()
    {
        var store = new AsyncPostgreSQLStore<CollapsedGroupRow>();
        store.SetSettings(_live.Settings());
        store.DestroyAsync().GetAwaiter().GetResult();
        return store;
    }

    protected override IBulkStore<CollapsedGroupRow> NewSyncStore()
    {
        var store = new PostgreSQLStore<CollapsedGroupRow>();
        store.SetSettings(_live.Settings());
        store.Destroy();
        return store;
    }
}
