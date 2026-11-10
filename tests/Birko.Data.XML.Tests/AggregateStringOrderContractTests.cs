using System;
using Birko.Data.Models;
using Birko.Data.Stores;
using Birko.Data.XML.Stores;
using Birko.Tests.Shared.StoreContract;

namespace Birko.Data.XML.Tests;

public class StringOrderRow : AbstractModel
{
    public string? Kind { get; set; }
    public string? Name { get; set; }
    public int N { get; set; }
}

/// <summary>TASK-907's aggregate string order contract (<c>tests/Shared/StoreContract</c>) on the XML stores.</summary>
public sealed class XmlAggregateStringOrderContractTests : AggregateStringOrderContract<StringOrderRow>, IDisposable
{
    /// <summary>The store aggregates in memory, through the oracle itself.</summary>
    protected override bool AnswersEveryComparison => true;

    private readonly TempDirs _dirs = new();

    protected override IAsyncBulkStore<StringOrderRow> NewAsyncStore()
    {
        var store = new AsyncXmlStore<StringOrderRow>();
        store.SetSettings(_dirs.NewSettings());
        return store;
    }

    protected override IBulkStore<StringOrderRow> NewSyncStore()
    {
        var store = new XmlStore<StringOrderRow>();
        store.SetSettings(_dirs.NewSettings());
        return store;
    }

    public void Dispose() => _dirs.Dispose();
}
