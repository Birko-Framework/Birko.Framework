using Birko.Data.SQL.PostgreSQL.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.SQL.PostgreSQL.Tests;

/// <summary>TASK-861's always-false-group contract on <see cref="PostgreSQLStore{T}"/> and <see cref="AsyncPostgreSQLStore{T}"/>, live.</summary>
public sealed class PostgreSQLAlwaysFalseGroupContractTests : AlwaysFalseGroupContract
{
    private readonly PostgreSQLLive _live;
    public PostgreSQLAlwaysFalseGroupContractTests(ITestOutputHelper output) => _live = new PostgreSQLLive(output);
    protected override bool Ready() => _live.Ready();

    protected override IAsyncBulkStore<AlwaysFalseRow> NewAsyncStore()
    {
        var store = new AsyncPostgreSQLStore<AlwaysFalseRow>();
        store.SetSettings(_live.Settings());
        store.DestroyAsync().GetAwaiter().GetResult();
        return store;
    }

    protected override IBulkStore<AlwaysFalseRow> NewSyncStore()
    {
        var store = new PostgreSQLStore<AlwaysFalseRow>();
        store.SetSettings(_live.Settings());
        store.Destroy();
        return store;
    }
}
