using System;
using System.IO;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>TASK-866's collapsed-group contract on <see cref="SQLiteStore{T}"/> and <see cref="AsyncSQLiteStore{T}"/>.</summary>
public sealed class SQLiteCollapsedGroupContractTests : CollapsedGroupContract, IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "birko-sqlite-866-" + Guid.NewGuid().ToString("N"));

    public SQLiteCollapsedGroupContractTests() => Directory.CreateDirectory(_root);

    protected override IAsyncBulkStore<CollapsedGroupRow> NewAsyncStore()
    {
        var store = new AsyncSQLiteStore<CollapsedGroupRow>();
        store.SetSettings(new SqLiteSettings(_root, $"t866-{Guid.NewGuid():N}.db"));
        return store;
    }

    protected override IBulkStore<CollapsedGroupRow> NewSyncStore()
    {
        var store = new SQLiteStore<CollapsedGroupRow>();
        store.SetSettings(new SqLiteSettings(_root, $"t866-{Guid.NewGuid():N}.db"));
        return store;
    }

    public void Dispose() => TestTempDirectory.Delete(_root);
}
