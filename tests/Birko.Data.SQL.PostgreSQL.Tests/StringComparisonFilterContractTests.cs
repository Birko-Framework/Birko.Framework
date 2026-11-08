using Birko.Data.SQL.PostgreSQL.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.SQL.PostgreSQL.Tests;

/// <summary>TASK-869's string-comparison contract on <see cref="AsyncPostgreSQLStore{T}"/>, live.</summary>
public sealed class PostgreSQLStringComparisonFilterContractTests : StringComparisonFilterContract
{
    private readonly PostgreSQLLive _live;
    public PostgreSQLStringComparisonFilterContractTests(ITestOutputHelper output) => _live = new PostgreSQLLive(output);
    protected override bool Ready() => _live.Ready();

    protected override IAsyncBulkStore<StringMatchRow> NewStore()
    {
        var store = new AsyncPostgreSQLStore<StringMatchRow>();
        store.SetSettings(_live.Settings());
        store.DestroyAsync().GetAwaiter().GetResult();
        return store;
    }

    /// <summary>A deterministic default collation: LIKE compares code points.</summary>
    protected override DefaultTextCollation Collation => DefaultTextCollation.Binary;
}
