using Birko.Data.InMemory.Stores;
using Birko.Data.Models;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;

namespace Birko.Data.InMemory.Tests;

public class AggregateEdgeRow : AbstractModel
{
    public string? Code { get; set; }
    public string? Kind { get; set; }
    public int N { get; set; }
    public int? Score { get; set; }
    public decimal? Price { get; set; }
}

/// <summary>TASK-886's aggregate edge contract (<c>tests/Shared/StoreContract</c>) on the in-memory stores.</summary>
public class InMemoryAggregateEdgeContractTests : AggregateEdgeContract<AggregateEdgeRow>
{
    protected override IAsyncBulkStore<AggregateEdgeRow> NewAsyncStore() => new AsyncInMemoryStore<AggregateEdgeRow>();
    protected override IBulkStore<AggregateEdgeRow> NewSyncStore() => new InMemoryStore<AggregateEdgeRow>();
}
