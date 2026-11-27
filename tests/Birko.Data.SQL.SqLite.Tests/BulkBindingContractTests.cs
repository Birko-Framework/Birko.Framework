using System;
using System.IO;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>TASK-1007's bulk-binding contract on <see cref="SQLiteStore{T}"/> and <see cref="AsyncSQLiteStore{T}"/>.</summary>
public sealed class SQLiteBulkBindingContractTests : BulkBindingContract, IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "birko-sqlite-1007-" + Guid.NewGuid().ToString("N"));

    public SQLiteBulkBindingContractTests() => Directory.CreateDirectory(_root);

    private SqLiteSettings Settings() => new(_root, $"t1007-{Guid.NewGuid():N}.db");

    protected override IAsyncBulkStore<BulkBindRow> NewAsyncRowStore() { var s = new AsyncSQLiteStore<BulkBindRow>(); s.SetSettings(Settings()); return s; }
    protected override IBulkStore<BulkBindRow> NewSyncRowStore() { var s = new SQLiteStore<BulkBindRow>(); s.SetSettings(Settings()); return s; }
    protected override IAsyncBulkStore<BulkBindKeyed> NewAsyncKeyedStore() { var s = new AsyncSQLiteStore<BulkBindKeyed>(); s.SetSettings(Settings()); return s; }
    protected override IBulkStore<BulkBindKeyed> NewSyncKeyedStore() { var s = new SQLiteStore<BulkBindKeyed>(); s.SetSettings(Settings()); return s; }

    public void Dispose() => TestTempDirectory.Delete(_root);
}
