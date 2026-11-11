using System;
using Birko.Data.CosmosDB.Stores;
using Birko.Data.Models;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.CosmosDB.Tests;

public class StringOrderDoc : AbstractModel
{
    public string? Kind { get; set; }
    public string? Name { get; set; }
    public int N { get; set; }
}

/// <summary>TASK-907's aggregate string order contract (<c>tests/Shared/StoreContract</c>) on the Cosmos DB stores, live.</summary>
public sealed class CosmosAggregateStringOrderContractTests : AggregateStringOrderContract<StringOrderDoc>, IDisposable
{
    private readonly CosmosLive _live;
    public CosmosAggregateStringOrderContractTests(ITestOutputHelper output) => _live = new CosmosLive(output);
    protected override bool Ready() => _live.Ready();

    /// <summary>Strings are ordered by code point: a group mixing U+E000–U+FFFF with an astral character is refused (TASK-907).</summary>
    protected override bool OrdersByCodePoint => true;

    /// <summary>The result rows are ordered in memory, after every group is read, so in every comparison (TASK-909).</summary>
    protected override bool OrdersRowsInEveryComparison => true;

    protected override IAsyncBulkStore<StringOrderDoc> NewAsyncStore()
    {
        var (conn, db, settings) = _live.Next();
        return new AsyncCosmosDBStore<StringOrderDoc>(conn, db, null, settings);
    }

    protected override IBulkStore<StringOrderDoc> NewSyncStore()
    {
        var (conn, db, settings) = _live.Next();
        return new CosmosDBStore<StringOrderDoc>(conn, db, null, settings);
    }

    public void Dispose() => _live.Dispose();
}
