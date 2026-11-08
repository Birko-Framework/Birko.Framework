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
