using Birko.Data.SQL.MSSql.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MSSql.Tests;

/// <summary>TASK-870's translation-leftovers contract on <see cref="AsyncMSSqlStore{T}"/>, live.</summary>
public sealed class SQLServerTranslationLeftoversContractTests : TranslationLeftoversContract
{
    private readonly SQLServerLive _live;
    public SQLServerTranslationLeftoversContractTests(ITestOutputHelper output) => _live = new SQLServerLive(output);
    protected override bool Ready() => _live.Ready();

    protected override IAsyncBulkStore<LeftoverRow> NewStore()
    {
        var store = new AsyncMSSqlStore<LeftoverRow>();
        store.SetSettings(_live.Settings());
        store.DestroyAsync().GetAwaiter().GetResult();
        return store;
    }

    /// <summary>
    /// TASK-870, measured 2026-11-09 on SQL Server 2022: <c>LOWER</c> / <c>UPPER</c> fold by the server's case tables, not .NET's. Left
    /// for a decision; see <see cref="TranslationLeftoversContract.FoldMismatchesAllowed"/>.
    /// </summary>
    private static readonly System.Collections.Generic.Dictionary<(FoldTest, string), int> MeasuredFoldGap = new()
    {
        [(FoldTest.Set, "ToLower")] = 7, [(FoldTest.Set, "ToUpper")] = 7, [(FoldTest.Set, "ToLowerInvariant")] = 8, [(FoldTest.Set, "ToUpperInvariant")] = 8,
        [(FoldTest.Ordinal, "ToLower")] = 15, [(FoldTest.Ordinal, "ToUpper")] = 17, [(FoldTest.Ordinal, "ToLowerInvariant")] = 17, [(FoldTest.Ordinal, "ToUpperInvariant")] = 19,
        [(FoldTest.Collation, "ToLower")] = 26, [(FoldTest.Collation, "ToUpper")] = 28, [(FoldTest.Collation, "ToLowerInvariant")] = 28, [(FoldTest.Collation, "ToUpperInvariant")] = 30,
    };

    protected override int FoldMismatchesAllowed(FoldTest test, string fold) => MeasuredFoldGap[(test, fold)];
}
