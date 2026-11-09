using System;
using System.IO;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>TASK-873's string-equality contract on <see cref="AsyncSQLiteStore{T}"/>.</summary>
public sealed class SQLiteStringEqualityContractTests : StringEqualityContract, IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "birko-sqlite-873-" + Guid.NewGuid().ToString("N"));

    public SQLiteStringEqualityContractTests() => Directory.CreateDirectory(_root);

    protected override IAsyncBulkStore<StringEqualityRow> NewStore()
    {
        var store = new AsyncSQLiteStore<StringEqualityRow>();
        store.SetSettings(new SqLiteSettings(_root, $"t873-{Guid.NewGuid():N}.db"));
        return store;
    }

    public void Dispose() => TestTempDirectory.Delete(_root);
}
