using System;
using Birko.Data.JSON.Stores;
using Birko.Data.Models;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;

namespace Birko.Data.JSON.Tests;

public class NullOrderingRow : AbstractModel
{
    public string? Code { get; set; }
    public int A { get; set; }
    public int? Score { get; set; }
    public int Amount { get; set; }
}

/// <summary>TASK-865's null-ordering scope contract (<c>tests/Shared/StoreContract</c>) on the JSON stores.</summary>
public sealed class JsonNullOrderingScopeContractTests : NullOrderingScopeContract<NullOrderingRow>, IDisposable
{
    private readonly TempDirs _dirs = new();

    protected override IAsyncBulkStore<NullOrderingRow> NewAsyncStore()
    {
        var store = new AsyncJsonStore<NullOrderingRow>();
        store.SetSettings(_dirs.NewSettings("birko-json-865-"));
        return store;
    }

    protected override IBulkStore<NullOrderingRow> NewSyncStore()
    {
        var store = new JsonStore<NullOrderingRow>();
        store.SetSettings(_dirs.NewSettings("birko-json-865-"));
        return store;
    }

    public void Dispose() => _dirs.Dispose();
}
