using System;
using System.Collections.Generic;
using System.IO;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Tests.Shared.StoreContract;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>TASK-864's rule NULL contract on <see cref="SQLiteStore{T}"/>.</summary>
public sealed class SQLiteRuleNullContractTests : RuleNullContract, IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "birko-sqlite-864-" + Guid.NewGuid().ToString("N"));

    public SQLiteRuleNullContractTests() => Directory.CreateDirectory(_root);

    protected override AbstractConnector Seeded(IReadOnlyList<RuleNullRow> rows)
    {
        var store = new SQLiteStore<RuleNullRow>();
        store.SetSettings(new SqLiteSettings(_root, $"t864-{Guid.NewGuid():N}.db"));
        store.Create(rows);
        return store.Connector;
    }

    public void Dispose() => TestTempDirectory.Delete(_root);
}
