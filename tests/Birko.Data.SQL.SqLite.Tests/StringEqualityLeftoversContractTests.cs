using System;
using System.IO;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>TASK-895's string-equality leftovers contract on <see cref="SQLiteStore{T}"/>.</summary>
public sealed class SQLiteStringEqualityLeftoversContractTests : StringEqualityLeftoversContract, IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "birko-sqlite-895-" + Guid.NewGuid().ToString("N"));

    public SQLiteStringEqualityLeftoversContractTests() => Directory.CreateDirectory(_root);

    protected override int MaxParameters => 32766;

    protected override (IBulkStore<StringLeftoverRow> Store, AbstractConnector Connector) NewStore()
    {
        var store = new SQLiteStore<StringLeftoverRow>();
        store.SetSettings(new SqLiteSettings(_root, $"t895-{Guid.NewGuid():N}.db"));
        return (store, store.Connector);
    }

    public void Dispose() => TestTempDirectory.Delete(_root);
}
