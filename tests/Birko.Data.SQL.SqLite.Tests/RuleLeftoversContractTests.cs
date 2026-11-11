using System;
using System.IO;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>TASK-882's rule leftovers contract on <see cref="SQLiteStore{T}"/>.</summary>
public sealed class SQLiteRuleLeftoversContractTests : RuleLeftoversContract, IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "birko-sqlite-882-" + Guid.NewGuid().ToString("N"));

    public SQLiteRuleLeftoversContractTests() => Directory.CreateDirectory(_root);

    protected override (IBulkStore<RuleLeftoverRow> Store, AbstractConnector Connector) NewStore()
    {
        var store = new SQLiteStore<RuleLeftoverRow>();
        store.SetSettings(new SqLiteSettings(_root, $"t882-{Guid.NewGuid():N}.db"));
        return (store, store.Connector);
    }

    public void Dispose() => TestTempDirectory.Delete(_root);
}
