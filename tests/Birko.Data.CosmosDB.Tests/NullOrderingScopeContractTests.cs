using System;
using Birko.Data.CosmosDB.Stores;
using Birko.Data.Models;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.CosmosDB.Tests;

public class NullOrderingDoc : AbstractModel
{
    public string? Code { get; set; }
    public int A { get; set; }
    public int? Score { get; set; }
    public int Amount { get; set; }
}

/// <summary>TASK-865's null-ordering scope contract (<c>tests/Shared/StoreContract</c>) on the Cosmos DB stores, live.</summary>
public sealed class CosmosNullOrderingScopeContractTests : NullOrderingScopeContract<NullOrderingDoc>, IDisposable
{
    private readonly CosmosLive _live;
    public CosmosNullOrderingScopeContractTests(ITestOutputHelper output) => _live = new CosmosLive(output);
    protected override bool Ready() => _live.Ready();

    /// <summary>Measured 2026-11-08 (TASK-865): Cosmos DB's <c>Score &gt; null</c> is undefined, and so is its NOT, so a
    /// NEGATED ordering against null matches nothing where C# matches every row — these filters miss rows, silently.
    /// Not fixed here (a read translation).</summary>
    protected override string? KnownTranslationGap(string shape) => shape switch
    {
        "!(Score > nul || A > 3)" or "!(Score > nul) && A > 4"
            => "NOT of an undefined comparison is undefined, so a negated null ordering filters the row out",
        _ => null,
    };

    protected override IAsyncBulkStore<NullOrderingDoc> NewAsyncStore()
    {
        var (conn, db, settings) = _live.Next();
        return new AsyncCosmosDBStore<NullOrderingDoc>(conn, db, null, settings);
    }

    protected override IBulkStore<NullOrderingDoc> NewSyncStore()
    {
        var (conn, db, settings) = _live.Next();
        return new CosmosDBStore<NullOrderingDoc>(conn, db, null, settings);
    }

    public void Dispose() => _live.Dispose();
}
