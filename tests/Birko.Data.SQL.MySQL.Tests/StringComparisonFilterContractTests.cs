using Birko.Data.SQL.MySQL.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MySQL.Tests;

/// <summary>TASK-869's string-comparison contract on <see cref="AsyncMySQLStore{T}"/>, live.</summary>
public sealed class MySQLStringComparisonFilterContractTests : StringComparisonFilterContract
{
    private readonly MySQLLive _live;
    public MySQLStringComparisonFilterContractTests(ITestOutputHelper output) => _live = new MySQLLive(output);
    protected override bool Ready() => _live.Ready();

    protected override IAsyncBulkStore<StringMatchRow> NewStore()
    {
        var store = new AsyncMySQLStore<StringMatchRow>();
        store.SetSettings(_live.Settings());
        store.DestroyAsync().GetAwaiter().GetResult();
        return store;
    }

    /// <summary>MySQL 8's default <c>utf8mb4_0900_ai_ci</c>.</summary>
    protected override DefaultTextCollation Collation => DefaultTextCollation.CaseAndAccentInsensitive;
}
