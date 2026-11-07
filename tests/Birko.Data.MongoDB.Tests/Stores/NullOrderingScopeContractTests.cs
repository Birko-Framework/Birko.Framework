using System;
using Birko.Data.MongoDB.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.MongoDB.Tests.Stores;

public class NullOrderingDoc : Birko.Data.MongoDB.Models.MongoDBModel
{
    public string? Code { get; set; }
    public int A { get; set; }
    public int? Score { get; set; }
    public int Amount { get; set; }
}

/// <summary>TASK-865's null-ordering scope contract (<c>tests/Shared/StoreContract</c>) on the MongoDB stores, live.</summary>
public sealed class MongoNullOrderingScopeContractTests : NullOrderingScopeContract<NullOrderingDoc>, IDisposable
{
    private readonly MongoLive _live;
    public MongoNullOrderingScopeContractTests(ITestOutputHelper output) => _live = new MongoLive(output);
    protected override bool Ready() => _live.Ready();

    protected override IAsyncBulkStore<NullOrderingDoc> NewAsyncStore()
    {
        var store = new AsyncMongoDBStore<NullOrderingDoc>();
        store.SetSettings(_live.NewSettings());
        return store;
    }

    protected override IBulkStore<NullOrderingDoc> NewSyncStore()
    {
        var store = new MongoDBStore<NullOrderingDoc>();
        store.SetSettings(_live.NewSettings());
        return store;
    }

    public void Dispose() => _live.Dispose();
}
