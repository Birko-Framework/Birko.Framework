using System;
using Birko.Data.ElasticSearch.Stores;
using Birko.Data.Models;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.ElasticSearch.Tests;

public class NullOrderingDoc : AbstractModel
{
    public string? Code { get; set; }
    public int A { get; set; }
    public int? Score { get; set; }
    public int Amount { get; set; }
}

/// <summary>TASK-865's null-ordering scope contract (<c>tests/Shared/StoreContract</c>) on the Elasticsearch stores, live.</summary>
public sealed class ElasticNullOrderingScopeContractTests : NullOrderingScopeContract<NullOrderingDoc>, IDisposable
{
    private readonly ElasticLive _live;
    public ElasticNullOrderingScopeContractTests(ITestOutputHelper output) => _live = new ElasticLive(output);
    protected override bool Ready() => _live.Ready();
    protected override void Settle() => ElasticLive.Refresh();

    protected override IAsyncBulkStore<NullOrderingDoc> NewAsyncStore()
    {
        var store = new AsyncElasticSearchStore<NullOrderingDoc>();
        store.SetSettings(_live.NewSettings());
        return store;
    }

    protected override IBulkStore<NullOrderingDoc> NewSyncStore()
    {
        var store = new ElasticSearchStore<NullOrderingDoc>();
        store.SetSettings(_live.NewSettings());
        return store;
    }

    public void Dispose() => _live.Dispose();
}
