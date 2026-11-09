using Birko.Data.SQL.MySQL.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MySQL.Tests;

/// <summary>TASK-870's translation-leftovers contract on <see cref="AsyncMySQLStore{T}"/>, live.</summary>
public sealed class MySQLTranslationLeftoversContractTests : TranslationLeftoversContract
{
    private readonly MySQLLive _live;
    public MySQLTranslationLeftoversContractTests(ITestOutputHelper output) => _live = new MySQLLive(output);
    protected override bool Ready() => _live.Ready();

    protected override IAsyncBulkStore<LeftoverRow> NewStore()
    {
        var store = new AsyncMySQLStore<LeftoverRow>();
        store.SetSettings(_live.Settings());
        store.DestroyAsync().GetAwaiter().GetResult();
        return store;
    }

    /// <summary>
    /// TASK-870, measured 2026-11-09 on MySQL 8.4: <c>LOWER</c> / <c>UPPER</c> fold by the server's case tables, not .NET's. Left
    /// for a decision; see <see cref="TranslationLeftoversContract.FoldMismatchesAllowed"/>.
    /// </summary>
    private static readonly System.Collections.Generic.Dictionary<(FoldTest, string), int> MeasuredFoldGap = new()
    {
        [(FoldTest.Set, "ToLower")] = 0, [(FoldTest.Set, "ToUpper")] = 1, [(FoldTest.Set, "ToLowerInvariant")] = 1, [(FoldTest.Set, "ToUpperInvariant")] = 2,
        [(FoldTest.Ordinal, "ToLower")] = 0, [(FoldTest.Ordinal, "ToUpper")] = 2, [(FoldTest.Ordinal, "ToLowerInvariant")] = 2, [(FoldTest.Ordinal, "ToUpperInvariant")] = 4,
        [(FoldTest.Collation, "ToLower")] = 22, [(FoldTest.Collation, "ToUpper")] = 23, [(FoldTest.Collation, "ToLowerInvariant")] = 23, [(FoldTest.Collation, "ToUpperInvariant")] = 24,
    };

    protected override int FoldMismatchesAllowed(FoldTest test, string fold) => MeasuredFoldGap[(test, fold)];
}
