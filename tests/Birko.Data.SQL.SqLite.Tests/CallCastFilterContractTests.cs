using System;
using System.IO;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>TASK-860's call-and-cast contract on <see cref="AsyncSQLiteStore{T}"/>.</summary>
public sealed class SQLiteCallCastFilterContractTests : CallCastFilterContract, IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "birko-sqlite-860-" + Guid.NewGuid().ToString("N"));

    public SQLiteCallCastFilterContractTests() => Directory.CreateDirectory(_root);

    protected override IAsyncBulkStore<CallCastRow> NewStore()
    {
        var store = new AsyncSQLiteStore<CallCastRow>();
        store.SetSettings(new SqLiteSettings(_root, $"t860-{Guid.NewGuid():N}.db"));
        return store;
    }

    /// <summary>Measured: SQLite stores an out-of-range integer silently (TASK-860), so it refuses a narrowing SET.</summary>
    protected override bool NarrowsIntegersInSet => false;

    public void Dispose() => TestTempDirectory.Delete(_root);
}
