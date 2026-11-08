using System;
using System.IO;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>TASK-869's string-comparison contract on <see cref="AsyncSQLiteStore{T}"/>.</summary>
public sealed class SQLiteStringComparisonFilterContractTests : StringComparisonFilterContract, IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "birko-sqlite-869-" + Guid.NewGuid().ToString("N"));

    public SQLiteStringComparisonFilterContractTests() => Directory.CreateDirectory(_root);

    protected override IAsyncBulkStore<StringMatchRow> NewStore()
    {
        var store = new AsyncSQLiteStore<StringMatchRow>();
        store.SetSettings(new SqLiteSettings(_root, $"t869-{Guid.NewGuid():N}.db"));
        return store;
    }

    /// <summary>SQLite's LIKE folds ASCII letters only.</summary>
    protected override DefaultTextCollation Collation => DefaultTextCollation.AsciiCaseInsensitive;

    public void Dispose() => TestTempDirectory.Delete(_root);
}
