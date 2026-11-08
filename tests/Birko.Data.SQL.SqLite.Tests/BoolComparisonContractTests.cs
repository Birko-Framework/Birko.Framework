using System;
using System.IO;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>TASK-863's boolean-against-a-constant contract on <see cref="AsyncSQLiteStore{T}"/>.</summary>
public sealed class SQLiteBoolComparisonContractTests : BoolComparisonContract, IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "birko-sqlite-863-" + Guid.NewGuid().ToString("N"));

    public SQLiteBoolComparisonContractTests() => Directory.CreateDirectory(_root);

    protected override IAsyncBulkStore<BoolComparisonRow> NewStore()
    {
        var store = new AsyncSQLiteStore<BoolComparisonRow>();
        store.SetSettings(new SqLiteSettings(_root, $"t863-{Guid.NewGuid():N}.db"));
        return store;
    }

    public void Dispose() => TestTempDirectory.Delete(_root);
}
