using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MySQL.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MySQL.Tests;

/// <summary>TASK-882's rule leftovers contract on <see cref="MySQLStore{T}"/>, live.</summary>
public sealed class MySQLRuleLeftoversContractTests : RuleLeftoversContract
{
    private readonly MySQLLive _live;
    public MySQLRuleLeftoversContractTests(ITestOutputHelper output) => _live = new MySQLLive(output);
    protected override bool Ready() => _live.Ready();

    protected override (IBulkStore<RuleLeftoverRow> Store, AbstractConnector Connector) NewStore()
    {
        var store = new MySQLStore<RuleLeftoverRow>();
        store.SetSettings(_live.Settings());
        store.Destroy();
        return (store, store.Connector);
    }
}
