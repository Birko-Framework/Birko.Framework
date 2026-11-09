using System.Collections.Generic;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.PostgreSQL.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.SQL.PostgreSQL.Tests;

/// <summary>TASK-864's rule NULL contract on <see cref="PostgreSQLStore{T}"/>, live.</summary>
public sealed class PostgreSQLRuleNullContractTests : RuleNullContract
{
    private readonly PostgreSQLLive _live;
    public PostgreSQLRuleNullContractTests(ITestOutputHelper output) => _live = new PostgreSQLLive(output);
    protected override bool Ready() => _live.Ready();

    protected override AbstractConnector Seeded(IReadOnlyList<RuleNullRow> rows)
    {
        var store = new PostgreSQLStore<RuleNullRow>();
        store.SetSettings(_live.Settings());
        store.Destroy();
        store.Create(rows);
        return store.Connector;
    }
}
