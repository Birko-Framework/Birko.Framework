using System.Collections.Generic;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MSSql.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MSSql.Tests;

/// <summary>TASK-864's rule NULL contract on <see cref="MSSqlStore{T}"/>, live.</summary>
public sealed class SQLServerRuleNullContractTests : RuleNullContract
{
    private readonly SQLServerLive _live;
    public SQLServerRuleNullContractTests(ITestOutputHelper output) => _live = new SQLServerLive(output);
    protected override bool Ready() => _live.Ready();

    protected override AbstractConnector Seeded(IReadOnlyList<RuleNullRow> rows)
    {
        var store = new MSSqlStore<RuleNullRow>();
        store.SetSettings(_live.Settings());
        store.Destroy();
        store.Create(rows);
        return store.Connector;
    }
}
