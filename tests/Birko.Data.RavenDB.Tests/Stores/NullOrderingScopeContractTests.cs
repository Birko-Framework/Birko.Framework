using System;
using Birko.Data.Models;
using Birko.Data.RavenDB.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.RavenDB.Tests.Stores;

public class NullOrderingDoc : AbstractModel
{
    public string? Code { get; set; }
    public int A { get; set; }
    public int? Score { get; set; }
    public int Amount { get; set; }
}

/// <summary>TASK-865's null-ordering scope contract (<c>tests/Shared/StoreContract</c>) on the RavenDB stores, live.</summary>
public sealed class RavenNullOrderingScopeContractTests : NullOrderingScopeContract<NullOrderingDoc>, IDisposable
{
    private readonly RavenLive _live;
    public RavenNullOrderingScopeContractTests(ITestOutputHelper output) => _live = new RavenLive(output);
    protected override bool Ready() => _live.Ready();

    protected override IAsyncBulkStore<NullOrderingDoc> NewAsyncStore() => _live.Track((url, db) => new AsyncRavenDBStore<NullOrderingDoc>(url, db));
    protected override IBulkStore<NullOrderingDoc> NewSyncStore() => _live.Track((url, db) => new RavenDBStore<NullOrderingDoc>(url, db));
    public void Dispose() => _live.Dispose();
}
