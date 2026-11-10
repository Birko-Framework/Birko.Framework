using Birko.Data.InMemory.Stores;
using Birko.Data.Models;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;

namespace Birko.Data.InMemory.Tests;

public class StringOrderRow : AbstractModel
{
    public string? Kind { get; set; }
    public string? Name { get; set; }
    public int N { get; set; }
}

/// <summary>TASK-907's aggregate string order contract (<c>tests/Shared/StoreContract</c>) on the in-memory stores.</summary>
public class InMemoryAggregateStringOrderContractTests : AggregateStringOrderContract<StringOrderRow>
{
    /// <summary>The store aggregates in memory, through the oracle itself.</summary>
    protected override bool AnswersEveryComparison => true;

    protected override IAsyncBulkStore<StringOrderRow> NewAsyncStore() => new AsyncInMemoryStore<StringOrderRow>();
    protected override IBulkStore<StringOrderRow> NewSyncStore() => new InMemoryStore<StringOrderRow>();
}
