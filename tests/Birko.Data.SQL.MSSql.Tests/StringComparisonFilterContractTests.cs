using Birko.Data.SQL.MSSql.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MSSql.Tests;

/// <summary>TASK-869's string-comparison contract on <see cref="AsyncMSSqlStore{T}"/>, live.</summary>
public sealed class SQLServerStringComparisonFilterContractTests : StringComparisonFilterContract
{
    private readonly SQLServerLive _live;
    public SQLServerStringComparisonFilterContractTests(ITestOutputHelper output) => _live = new SQLServerLive(output);
    protected override bool Ready() => _live.Ready();

    protected override IAsyncBulkStore<StringMatchRow> NewStore()
    {
        var store = new AsyncMSSqlStore<StringMatchRow>();
        store.SetSettings(_live.Settings());
        store.DestroyAsync().GetAwaiter().GetResult();
        return store;
    }

    /// <summary>The default <c>SQL_Latin1_General_CP1_CI_AS</c>.</summary>
    protected override DefaultTextCollation Collation => DefaultTextCollation.CaseInsensitive;

    /// <summary>SQL Server's LIKE has no class for a surrogate pair, so such a term is refused.</summary>
    protected override bool MatchesSupplementaryCaseClasses => false;

    /// <summary>SQL Server's LIKE pattern limit (Msg 8152 past it, measured).</summary>
    protected override int? MaxLikePatternLength => 4000;
}
