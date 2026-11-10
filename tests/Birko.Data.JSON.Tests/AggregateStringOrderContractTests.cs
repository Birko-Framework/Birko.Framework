using System;
using Birko.Data.JSON.Stores;
using Birko.Data.Models;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;

namespace Birko.Data.JSON.Tests;

public class StringOrderRow : AbstractModel
{
    public string? Kind { get; set; }
    public string? Name { get; set; }
    public int N { get; set; }
}

/// <summary>TASK-907's aggregate string order contract (<c>tests/Shared/StoreContract</c>) on the JSON stores.</summary>
public sealed class JsonAggregateStringOrderContractTests : AggregateStringOrderContract<StringOrderRow>, IDisposable
{
    /// <summary>The store aggregates in memory, through the oracle itself.</summary>
    protected override bool AnswersEveryComparison => true;

    private readonly TempDirs _dirs = new();

    protected override IAsyncBulkStore<StringOrderRow> NewAsyncStore()
    {
        var store = new AsyncJsonStore<StringOrderRow>();
        store.SetSettings(_dirs.NewSettings("birko-json-907-"));
        return store;
    }

    protected override IBulkStore<StringOrderRow> NewSyncStore()
    {
        var store = new JsonStore<StringOrderRow>();
        store.SetSettings(_dirs.NewSettings("birko-json-907-"));
        return store;
    }

    public void Dispose() => _dirs.Dispose();
}
