using System;
using Birko.Data.MongoDB.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.MongoDB.Tests.Stores;

public class StringOrderDoc : Birko.Data.MongoDB.Models.MongoDBModel
{
    public string? Kind { get; set; }
    public string? Name { get; set; }
    public int N { get; set; }
}

/// <summary>TASK-907's aggregate string order contract (<c>tests/Shared/StoreContract</c>) on the MongoDB stores, live.</summary>
public sealed class MongoAggregateStringOrderContractTests : AggregateStringOrderContract<StringOrderDoc>, IDisposable
{
    private readonly MongoLive _live;
    public MongoAggregateStringOrderContractTests(ITestOutputHelper output) => _live = new MongoLive(output);
    protected override bool Ready() => _live.Ready();

    /// <summary>Strings are ordered by code point: a group mixing U+E000–U+FFFF with an astral character is refused (TASK-907).</summary>
    protected override bool OrdersByCodePoint => true;

    /// <summary>The result rows are ordered in memory, after every group is read, so in every comparison (TASK-909).</summary>
    protected override bool OrdersRowsInEveryComparison => true;

    protected override IAsyncBulkStore<StringOrderDoc> NewAsyncStore()
    {
        var store = new AsyncMongoDBStore<StringOrderDoc>();
        store.SetSettings(_live.NewSettings());
        return store;
    }

    protected override IBulkStore<StringOrderDoc> NewSyncStore()
    {
        var store = new MongoDBStore<StringOrderDoc>();
        store.SetSettings(_live.NewSettings());
        return store;
    }

    public void Dispose() => _live.Dispose();
}
