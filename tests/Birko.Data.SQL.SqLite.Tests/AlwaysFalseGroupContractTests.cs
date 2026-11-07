using System;
using System.IO;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>TASK-861's always-false-group contract on <see cref="SQLiteStore{T}"/> and <see cref="AsyncSQLiteStore{T}"/>.</summary>
public sealed class SQLiteAlwaysFalseGroupContractTests : AlwaysFalseGroupContract, IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "birko-sqlite-861-" + Guid.NewGuid().ToString("N"));

    public SQLiteAlwaysFalseGroupContractTests() => Directory.CreateDirectory(_root);

    protected override IAsyncBulkStore<AlwaysFalseRow> NewAsyncStore()
    {
        var store = new AsyncSQLiteStore<AlwaysFalseRow>();
        store.SetSettings(new SqLiteSettings(_root, $"t861-{Guid.NewGuid():N}.db"));
        return store;
    }

    protected override IBulkStore<AlwaysFalseRow> NewSyncStore()
    {
        var store = new SQLiteStore<AlwaysFalseRow>();
        store.SetSettings(new SqLiteSettings(_root, $"t861-{Guid.NewGuid():N}.db"));
        return store;
    }

    public void Dispose() => TestTempDirectory.Delete(_root);
}
