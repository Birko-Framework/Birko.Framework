using System;
using System.IO;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>TASK-857's math-filter contract on <see cref="AsyncSQLiteStore{T}"/>: decimal steps run in SQLite's exact decimal functions.</summary>
public sealed class SQLiteMathFilterContractTests : MathFilterContract, IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "birko-sqlite-857-" + Guid.NewGuid().ToString("N"));

    public SQLiteMathFilterContractTests() => Directory.CreateDirectory(_root);

    protected override IAsyncBulkStore<MathFilterRow> NewStore()
    {
        var store = new AsyncSQLiteStore<MathFilterRow>();
        store.SetSettings(new SqLiteSettings(_root, $"t857-{Guid.NewGuid():N}.db"));
        return store;
    }

    public void Dispose() => TestTempDirectory.Delete(_root);
}
