using System.Collections.Generic;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MySQL.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MySQL.Tests;

/// <summary>TASK-864's rule NULL contract on <see cref="MySQLStore{T}"/>, live.</summary>
public sealed class MySQLRuleNullContractTests : RuleNullContract
{
    private readonly MySQLLive _live;
    public MySQLRuleNullContractTests(ITestOutputHelper output) => _live = new MySQLLive(output);
    protected override bool Ready() => _live.Ready();

    protected override AbstractConnector Seeded(IReadOnlyList<RuleNullRow> rows)
    {
        var store = new MySQLStore<RuleNullRow>();
        store.SetSettings(_live.Settings());
        store.Destroy();
        store.Create(rows);
        return store.Connector;
    }
}
