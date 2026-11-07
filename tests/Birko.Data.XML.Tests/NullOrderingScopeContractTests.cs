using System;
using Birko.Data.Models;
using Birko.Data.Stores;
using Birko.Data.XML.Stores;
using Birko.Tests.Shared.StoreContract;

namespace Birko.Data.XML.Tests;

public class NullOrderingRow : AbstractModel
{
    public string? Code { get; set; }
    public int A { get; set; }
    public int? Score { get; set; }
    public int Amount { get; set; }
}

/// <summary>TASK-865's null-ordering scope contract (<c>tests/Shared/StoreContract</c>) on the XML stores.</summary>
public sealed class XmlNullOrderingScopeContractTests : NullOrderingScopeContract<NullOrderingRow>, IDisposable
{
    private readonly TempDirs _dirs = new();

    protected override IAsyncBulkStore<NullOrderingRow> NewAsyncStore()
    {
        var store = new AsyncXmlStore<NullOrderingRow>();
        store.SetSettings(_dirs.NewSettings());
        return store;
    }

    protected override IBulkStore<NullOrderingRow> NewSyncStore()
    {
        var store = new XmlStore<NullOrderingRow>();
        store.SetSettings(_dirs.NewSettings());
        return store;
    }

    public void Dispose() => _dirs.Dispose();
}
