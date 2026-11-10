using System;
using System.IO;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;

namespace Birko.Data.SQL.SqLite.Tests;

[Table("T886AggregateEdgeRows")]
public class AggregateEdgeRow : AbstractModel
{
    public string? Code { get; set; }
    public string? Kind { get; set; }
    public int N { get; set; }
    public int? Score { get; set; }
    public decimal? Price { get; set; }
}

/// <summary>TASK-886's aggregate edge contract (<c>tests/Shared/StoreContract</c>) on the SQLite stores, a file per store.</summary>
public sealed class SQLiteAggregateEdgeContractTests : AggregateEdgeContract<AggregateEdgeRow>, IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "birko-sqlite-886-" + Guid.NewGuid().ToString("N"));

    public SQLiteAggregateEdgeContractTests() => Directory.CreateDirectory(_root);

    protected override IAsyncBulkStore<AggregateEdgeRow> NewAsyncStore()
    {
        var store = new AsyncSQLiteStore<AggregateEdgeRow>();
        store.SetSettings(new SqLiteSettings(_root, $"t886-{Guid.NewGuid():N}.db"));
        return store;
    }

    protected override IBulkStore<AggregateEdgeRow> NewSyncStore()
    {
        var store = new SQLiteStore<AggregateEdgeRow>();
        store.SetSettings(new SqLiteSettings(_root, $"t886-{Guid.NewGuid():N}.db"));
        return store;
    }

    public void Dispose() => TestTempDirectory.Delete(_root);
}
