using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.PostgreSQL.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.SQL.PostgreSQL.Tests;

/// <summary>TASK-882's rule leftovers contract on <see cref="PostgreSQLStore{T}"/>, live.</summary>
public sealed class PostgreSQLRuleLeftoversContractTests : RuleLeftoversContract
{
    private readonly PostgreSQLLive _live;
    public PostgreSQLRuleLeftoversContractTests(ITestOutputHelper output) => _live = new PostgreSQLLive(output);
    protected override bool Ready() => _live.Ready();

    protected override (IBulkStore<RuleLeftoverRow> Store, AbstractConnector Connector) NewStore()
    {
        var store = new PostgreSQLStore<RuleLeftoverRow>();
        store.SetSettings(_live.Settings());
        store.Destroy();
        return (store, store.Connector);
    }
}
