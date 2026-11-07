using System;
using System.IO;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>TASK-859's nullable-filter contract on <see cref="AsyncSQLiteStore{T}"/>.</summary>
public sealed class SQLiteNullFilterContractTests : NullFilterContract, IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "birko-sqlite-859-" + Guid.NewGuid().ToString("N"));

    public SQLiteNullFilterContractTests() => Directory.CreateDirectory(_root);

    protected override IAsyncBulkStore<NullFilterRow> NewStore()
    {
        var store = new AsyncSQLiteStore<NullFilterRow>();
        store.SetSettings(new SqLiteSettings(_root, $"t859-{Guid.NewGuid():N}.db"));
        return store;
    }

    public void Dispose() => TestTempDirectory.Delete(_root);
}
