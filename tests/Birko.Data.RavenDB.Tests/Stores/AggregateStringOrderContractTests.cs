using System;
using Birko.Data.Models;
using Birko.Data.RavenDB.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.RavenDB.Tests.Stores;

public class StringOrderDoc : AbstractModel
{
    public string? Kind { get; set; }
    public string? Name { get; set; }
    public int N { get; set; }
}

/// <summary>TASK-907's aggregate string order contract (<c>tests/Shared/StoreContract</c>) on the RavenDB stores, live.</summary>
public sealed class RavenAggregateStringOrderContractTests : AggregateStringOrderContract<StringOrderDoc>, IDisposable
{
    /// <summary>The store aggregates in memory, through the oracle itself.</summary>
    protected override bool AnswersEveryComparison => true;

    private readonly RavenLive _live;
    public RavenAggregateStringOrderContractTests(ITestOutputHelper output) => _live = new RavenLive(output);
    protected override bool Ready() => _live.Ready();

    protected override IAsyncBulkStore<StringOrderDoc> NewAsyncStore() => _live.Track((url, db) => new AsyncRavenDBStore<StringOrderDoc>(url, db));
    protected override IBulkStore<StringOrderDoc> NewSyncStore() => _live.Track((url, db) => new RavenDBStore<StringOrderDoc>(url, db));
    public void Dispose() => _live.Dispose();
}
