using System;
using Birko.Data.JSON.Stores;
using Birko.Data.Models;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;

namespace Birko.Data.JSON.Tests;

public class AggregateEdgeRow : AbstractModel
{
    public string? Code { get; set; }
    public string? Kind { get; set; }
    public int N { get; set; }
    public int? Score { get; set; }
    public decimal? Price { get; set; }
}

/// <summary>TASK-886's aggregate edge contract (<c>tests/Shared/StoreContract</c>) on the JSON stores.</summary>
public sealed class JsonAggregateEdgeContractTests : AggregateEdgeContract<AggregateEdgeRow>, IDisposable
{
    private readonly TempDirs _dirs = new();

    protected override IAsyncBulkStore<AggregateEdgeRow> NewAsyncStore()
    {
        var store = new AsyncJsonStore<AggregateEdgeRow>();
        store.SetSettings(_dirs.NewSettings("birko-json-886-"));
        return store;
    }

    protected override IBulkStore<AggregateEdgeRow> NewSyncStore()
    {
        var store = new JsonStore<AggregateEdgeRow>();
        store.SetSettings(_dirs.NewSettings("birko-json-886-"));
        return store;
    }

    public void Dispose() => _dirs.Dispose();
}
