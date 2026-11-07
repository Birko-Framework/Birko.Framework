using Birko.Data.SQL.PostgreSQL.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.SQL.PostgreSQL.Tests;

/// <summary>TASK-857's math-filter contract on <see cref="AsyncPostgreSQLStore{T}"/>, live.</summary>
public sealed class PostgreSQLMathFilterContractTests : MathFilterContract
{
    private readonly PostgreSQLLive _live;
    public PostgreSQLMathFilterContractTests(ITestOutputHelper output) => _live = new PostgreSQLLive(output);
    protected override bool Ready() => _live.Ready();

    protected override IAsyncBulkStore<MathFilterRow> NewStore()
    {
        var store = new AsyncPostgreSQLStore<MathFilterRow>();
        store.SetSettings(_live.Settings());
        store.DestroyAsync().GetAwaiter().GetResult();
        return store;
    }
}
