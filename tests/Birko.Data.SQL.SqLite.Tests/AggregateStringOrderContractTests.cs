using System;
using System.IO;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;

namespace Birko.Data.SQL.SqLite.Tests;

[Table("T886StringOrderRows")]
public class StringOrderRow : AbstractModel
{
    public string? Kind { get; set; }
    public string? Name { get; set; }
    public int N { get; set; }
}

/// <summary>TASK-907's aggregate string order contract (<c>tests/Shared/StoreContract</c>) on the SQLite stores, a file per store.</summary>
public sealed class SQLiteAggregateStringOrderContractTests : AggregateStringOrderContract<StringOrderRow>, IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "birko-sqlite-907-" + Guid.NewGuid().ToString("N"));

    public SQLiteAggregateStringOrderContractTests() => Directory.CreateDirectory(_root);

    protected override IAsyncBulkStore<StringOrderRow> NewAsyncStore()
    {
        var store = new AsyncSQLiteStore<StringOrderRow>();
        store.SetSettings(new SqLiteSettings(_root, $"t907-{Guid.NewGuid():N}.db"));
        return store;
    }

    protected override IBulkStore<StringOrderRow> NewSyncStore()
    {
        var store = new SQLiteStore<StringOrderRow>();
        store.SetSettings(new SqLiteSettings(_root, $"t907-{Guid.NewGuid():N}.db"));
        return store;
    }

    public void Dispose() => TestTempDirectory.Delete(_root);
}
