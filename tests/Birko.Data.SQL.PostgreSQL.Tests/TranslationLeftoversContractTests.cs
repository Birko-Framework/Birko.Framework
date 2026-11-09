using Birko.Data.SQL.PostgreSQL.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.SQL.PostgreSQL.Tests;

/// <summary>TASK-870's translation-leftovers contract on <see cref="AsyncPostgreSQLStore{T}"/>, live.</summary>
public sealed class PostgreSQLTranslationLeftoversContractTests : TranslationLeftoversContract
{
    private readonly PostgreSQLLive _live;
    public PostgreSQLTranslationLeftoversContractTests(ITestOutputHelper output) => _live = new PostgreSQLLive(output);
    protected override bool Ready() => _live.Ready();

    protected override IAsyncBulkStore<LeftoverRow> NewStore()
    {
        var store = new AsyncPostgreSQLStore<LeftoverRow>();
        store.SetSettings(_live.Settings());
        store.DestroyAsync().GetAwaiter().GetResult();
        return store;
    }

    /// <summary>
    /// TASK-870, measured 2026-11-09 on PostgreSQL 16: <c>LOWER</c> / <c>UPPER</c> fold by the server's case tables, not .NET's. Left
    /// for a decision; see <see cref="TranslationLeftoversContract.FoldMismatchesAllowed"/>.
    /// </summary>
    private static readonly System.Collections.Generic.Dictionary<(FoldTest, string), int> MeasuredFoldGap = new()
    {
        [(FoldTest.Set, "ToLower")] = 0, [(FoldTest.Set, "ToUpper")] = 0, [(FoldTest.Set, "ToLowerInvariant")] = 1, [(FoldTest.Set, "ToUpperInvariant")] = 1,
        [(FoldTest.Ordinal, "ToLower")] = 0, [(FoldTest.Ordinal, "ToUpper")] = 0, [(FoldTest.Ordinal, "ToLowerInvariant")] = 2, [(FoldTest.Ordinal, "ToUpperInvariant")] = 2,
        [(FoldTest.Collation, "ToLower")] = 0, [(FoldTest.Collation, "ToUpper")] = 0, [(FoldTest.Collation, "ToLowerInvariant")] = 2, [(FoldTest.Collation, "ToUpperInvariant")] = 2,
    };

    protected override int FoldMismatchesAllowed(FoldTest test, string fold) => MeasuredFoldGap[(test, fold)];
}
