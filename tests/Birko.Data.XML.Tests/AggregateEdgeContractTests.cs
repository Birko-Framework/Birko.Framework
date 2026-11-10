using System;
using Birko.Data.Models;
using Birko.Data.Stores;
using Birko.Data.XML.Stores;
using Birko.Tests.Shared.StoreContract;

namespace Birko.Data.XML.Tests;

public class AggregateEdgeRow : AbstractModel
{
    public string? Code { get; set; }
    public string? Kind { get; set; }
    public int N { get; set; }
    public int? Score { get; set; }
    public decimal? Price { get; set; }
}

/// <summary>TASK-886's aggregate edge contract (<c>tests/Shared/StoreContract</c>) on the XML stores.</summary>
public sealed class XmlAggregateEdgeContractTests : AggregateEdgeContract<AggregateEdgeRow>, IDisposable
{
    private readonly TempDirs _dirs = new();

    protected override IAsyncBulkStore<AggregateEdgeRow> NewAsyncStore()
    {
        var store = new AsyncXmlStore<AggregateEdgeRow>();
        store.SetSettings(_dirs.NewSettings());
        return store;
    }

    protected override IBulkStore<AggregateEdgeRow> NewSyncStore()
    {
        var store = new XmlStore<AggregateEdgeRow>();
        store.SetSettings(_dirs.NewSettings());
        return store;
    }

    public void Dispose() => _dirs.Dispose();
}
