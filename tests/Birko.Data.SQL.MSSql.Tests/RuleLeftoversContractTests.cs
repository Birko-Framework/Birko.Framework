using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MSSql.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MSSql.Tests;

/// <summary>TASK-882's rule leftovers contract on <see cref="MSSqlStore{T}"/>, live.</summary>
public sealed class SQLServerRuleLeftoversContractTests : RuleLeftoversContract
{
    private readonly SQLServerLive _live;
    public SQLServerRuleLeftoversContractTests(ITestOutputHelper output) => _live = new SQLServerLive(output);
    protected override bool Ready() => _live.Ready();

    protected override (IBulkStore<RuleLeftoverRow> Store, AbstractConnector Connector) NewStore()
    {
        var store = new MSSqlStore<RuleLeftoverRow>();
        store.SetSettings(_live.Settings());
        store.Destroy();
        return (store, store.Connector);
    }
}
