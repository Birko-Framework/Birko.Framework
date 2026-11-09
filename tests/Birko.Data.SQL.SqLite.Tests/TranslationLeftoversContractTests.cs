using System;
using System.IO;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>TASK-870's translation-leftovers contract on <see cref="AsyncSQLiteStore{T}"/>.</summary>
public sealed class SQLiteTranslationLeftoversContractTests : TranslationLeftoversContract, IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "birko-sqlite-870-" + Guid.NewGuid().ToString("N"));

    public SQLiteTranslationLeftoversContractTests() => Directory.CreateDirectory(_root);

    protected override IAsyncBulkStore<LeftoverRow> NewStore()
    {
        var store = new AsyncSQLiteStore<LeftoverRow>();
        store.SetSettings(new SqLiteSettings(_root, $"t870-{Guid.NewGuid():N}.db"));
        return store;
    }

    /// <summary>SQLite divides a decimal in C#'s own decimal (TASK-856's functions): measured 0 of 32 shapes off.</summary>
    protected override bool DividesDecimalsExactly => true;

    public void Dispose() => TestTempDirectory.Delete(_root);
}
