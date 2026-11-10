using System;
using Birko.Data.ElasticSearch.Stores;
using Birko.Data.Models;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.ElasticSearch.Tests;

public class StringOrderDoc : AbstractModel
{
    public string? Kind { get; set; }
    public string? Name { get; set; }
    public int N { get; set; }
}

/// <summary>TASK-907's aggregate string order contract (<c>tests/Shared/StoreContract</c>) on the Elasticsearch stores, live.</summary>
public sealed class ElasticAggregateStringOrderContractTests : AggregateStringOrderContract<StringOrderDoc>, IDisposable
{
    private readonly ElasticLive _live;
    public ElasticAggregateStringOrderContractTests(ITestOutputHelper output) => _live = new ElasticLive(output);
    protected override bool Ready() => _live.Ready();
    protected override void Settle() => ElasticLive.Refresh();

    /// <summary>Strings are ordered by code point: a group mixing U+E000–U+FFFF with an astral character is refused (TASK-907).</summary>
    protected override bool OrdersByCodePoint => true;

    protected override IAsyncBulkStore<StringOrderDoc> NewAsyncStore()
    {
        var store = new AsyncElasticSearchStore<StringOrderDoc>();
        store.SetSettings(_live.NewSettings());
        return store;
    }

    protected override IBulkStore<StringOrderDoc> NewSyncStore()
    {
        var store = new ElasticSearchStore<StringOrderDoc>();
        store.SetSettings(_live.NewSettings());
        return store;
    }

    public void Dispose() => _live.Dispose();
}
