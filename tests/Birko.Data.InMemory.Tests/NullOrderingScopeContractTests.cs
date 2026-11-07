using Birko.Data.InMemory.Stores;
using Birko.Data.Models;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;

namespace Birko.Data.InMemory.Tests;

public class NullOrderingRow : AbstractModel
{
    public string? Code { get; set; }
    public int A { get; set; }
    public int? Score { get; set; }
    public int Amount { get; set; }
}

/// <summary>TASK-865's null-ordering scope contract (<c>tests/Shared/StoreContract</c>) on the in-memory stores.</summary>
public class InMemoryNullOrderingScopeContractTests : NullOrderingScopeContract<NullOrderingRow>
{
    protected override IAsyncBulkStore<NullOrderingRow> NewAsyncStore() => new AsyncInMemoryStore<NullOrderingRow>();
    protected override IBulkStore<NullOrderingRow> NewSyncStore() => new InMemoryStore<NullOrderingRow>();
}
